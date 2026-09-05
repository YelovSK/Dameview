using System.Drawing;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Dameview.UI;

internal readonly record struct UiKeyEvent(uint Key, bool Shift = false);

internal interface IModalContent : IUiElement
{
    public SizeF PreferredSize { get; }
    public bool DismissOnBackdrop => true;
    public bool DismissOnEscape => true;
    public void Focus();
    public void HandleKey(UiKeyEvent input);
    public RectangleF GetFocusBounds(SizeF size);
}

// Owns interaction and placement, not the lifetime of the supplied content.
internal sealed class ModalHost : IUiElement
{
    private readonly UiPointerRouter _pointerRouter = new();
    private IModalContent? _content;
    private Action? _restoreFocus;
    private float _scroll;
    private float _dpi;
    private SizeF _size;
    private bool _backdropPressed;

    internal ModalHost(float dpi)
    {
        _dpi = dpi;
    }

    internal void SetDpi(float dpi)
    {
        _scroll *= dpi / _dpi;
        _dpi = dpi;
    }

    internal bool IsOpen => _content is not null;

    internal void Show(IModalContent content, Action restoreFocus)
    {
        Close();
        _content = content;
        _restoreFocus = restoreFocus;
        _scroll = 0;
        content.Focus();
    }

    internal void Close()
    {
        _pointerRouter.Cancel();
        _content?.HandlePointer(new UiPointerEvent(UiPointerEventKind.Cancelled, PointF.Empty), SizeF.Empty);
        _content = null;
        _backdropPressed = false;
        Action? restore = _restoreFocus;
        _restoreFocus = null;
        restore?.Invoke();
    }

    internal bool HandleKey(UiKeyEvent input)
    {
        if (_content is not { } content)
        {
            return false;
        }

        if (input.Key == 0x1B && content.DismissOnEscape)
        {
            Close();
        }
        else
        {
            content.HandleKey(input);
            if (_content is not null)
            {
                RectangleF viewport = GetViewport(_size);
                RectangleF focus = content.GetFocusBounds(GetContentBounds(viewport).Size);
                if (focus.Top < _scroll)
                {
                    _scroll = focus.Top;
                }
                else if (focus.Bottom > _scroll + viewport.Height)
                {
                    _scroll = focus.Bottom - viewport.Height;
                }
            }
        }

        return true;
    }

    public bool Update(in UiUpdateContext context) => _content?.Update(context) ?? false;

    public void Draw(in UiDrawContext context, SizeF size)
    {
        _size = size;
        if (_content is not { } content)
        {
            return;
        }

        context.FillRoundedRectangle(new RoundedRectangle(
            new RectangleF(0, 0, context.PixelsToDips(size.Width), context.PixelsToDips(size.Height)), 0, 0),
            new Color4(0, 0, 0, 0.45f));
        RectangleF viewport = GetViewport(size);
        context.FillRoundedRectangle(new RoundedRectangle(context.PixelsToDips(viewport), 12, 12), context.Palette.Surface);
        context.DrawRoundedRectangle(new RoundedRectangle(context.PixelsToDips(viewport), 12, 12), context.Palette.SurfaceBorder);
        RectangleF clip = context.PixelsToDips(viewport);
        context.RenderTarget.PushAxisAlignedClip(new Rect(clip.X, clip.Y, clip.Width, clip.Height), AntialiasMode.Aliased);
        try
        {
            context.DrawElement(content, GetContentBounds(viewport));
        }
        finally
        {
            context.RenderTarget.PopAxisAlignedClip();
        }
    }

    public UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size)
    {
        if (_content is not { } content)
        {
            return default;
        }

        RectangleF viewport = GetViewport(size);
        RectangleF bounds = GetContentBounds(viewport);
        if (input.Kind == UiPointerEventKind.Cancelled)
        {
            _backdropPressed = false;
            _pointerRouter.Cancel();
            content.HandlePointer(input, bounds.Size);
        }
        else if (_pointerRouter.Captured is not null)
        {
            _pointerRouter.DispatchCaptured(input, bounds);
        }
        else if (input.Kind == UiPointerEventKind.Wheel && viewport.Contains(input.Position))
        {
            _scroll = Math.Clamp(_scroll - input.WheelDelta / 120f * 48 * UiDpi.GetScale(_dpi),
                0, MathF.Max(0, bounds.Height - viewport.Height));
        }
        else if (input.Kind == UiPointerEventKind.Released && _backdropPressed)
        {
            _backdropPressed = false;
            if (!viewport.Contains(input.Position) && content.DismissOnBackdrop)
            {
                Close();
            }
        }
        else if (input.Kind == UiPointerEventKind.Pressed && !viewport.Contains(input.Position))
        {
            _backdropPressed = input.Button == PointerButton.Primary;
        }
        else if (viewport.Contains(input.Position))
        {
            _pointerRouter.Route(content, bounds, input);
        }
        else if (input.Kind == UiPointerEventKind.Moved)
        {
            content.HandlePointer(new UiPointerEvent(UiPointerEventKind.Cancelled, PointF.Empty), bounds.Size);
        }

        return new UiPointerResult(Consumed: true, NeedsRepaint: true);
    }

    private RectangleF GetViewport(SizeF size)
    {
        float scale = UiDpi.GetScale(_dpi);
        SizeF desired = _content!.PreferredSize;
        float width = MathF.Min(desired.Width * scale, MathF.Max(0, size.Width - 24 * scale));
        float height = MathF.Min(desired.Height * scale, MathF.Max(0, size.Height - 24 * scale));
        return new RectangleF((size.Width - width) / 2, (size.Height - height) / 2, width, height);
    }

    private RectangleF GetContentBounds(RectangleF viewport)
    {
        float height = _content!.PreferredSize.Height * UiDpi.GetScale(_dpi);
        _scroll = Math.Clamp(_scroll, 0, MathF.Max(0, height - viewport.Height));
        return new RectangleF(viewport.X, viewport.Y - _scroll, viewport.Width, height);
    }
}
