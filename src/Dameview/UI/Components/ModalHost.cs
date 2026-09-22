using System.Drawing;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal abstract class ModalContent : UiElement
{
    internal abstract SizeF PreferredSize { get; }
    internal virtual bool DismissOnBackdrop => true;
    internal virtual bool DismissOnEscape => true;
    internal abstract UiElement InitialFocus { get; }
    internal override bool PreservesFocusOnPointerPress => true;
}

// Owns modal interaction and placement, not the lifetime of its content.
internal sealed class ModalHost : UiElement
{
    private const double VisibilityResponse = 22.0;
    private const float BackdropAlpha = 0.45f;
    private const float ClosedScale = 0.96f;

    private readonly ModalSurface _surface;
    // 1 while a modal is open. Easing it fades the backdrop and fades and scales the panel.
    private readonly AnimatedFloat _visibility = new(0.0f, VisibilityResponse);
    private Action? _dismiss;
    private bool _backdropPressed;

    internal ModalHost()
    {
        _surface = new ModalSurface(this);
        AddChild(_surface);
        IsVisible = false;
    }

    internal bool IsOpen => Content is not null;
    internal ModalContent? Content { get; private set; }
    internal override bool PreservesFocusOnPointerPress => true;
    // A closing modal is still drawn while it fades out, but clicks already reach what is beneath it.
    internal override bool IsHitTestVisible => IsOpen;

    internal void Show(ModalContent content, Action dismiss)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(dismiss);
        Root?.ClearPointer();
        _surface.SetContent(content);
        Content = content;
        _dismiss = dismiss;
        _backdropPressed = false;
        IsVisible = true;
        _visibility.SetTarget(1.0f);
        InvalidateVisual();
    }

    internal void Close()
    {
        if (Content is null)
        {
            return;
        }

        Root?.ClearPointer();
        Root?.DisconnectSubtree(_surface);
        Content = null;
        _dismiss = null;
        _backdropPressed = false;
        _visibility.SetTarget(0.0f);
        InvalidateVisual();
    }

    internal bool HandleEscape()
    {
        if (Content is null)
        {
            return false;
        }

        if (Content.DismissOnEscape)
        {
            _dismiss!();
        }
        else
        {
            Content.OnKeyEvent(new WindowKeyEvent(WindowKey.Escape));
        }

        return true;
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        if (_surface.Content is not null)
        {
            _surface.Measure(availableSize);
        }

        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        if (_surface.Content is not { } content)
        {
            return;
        }

        const float margin = 12.0f;
        SizeF desired = content.PreferredSize;
        float width = MathF.Min(desired.Width, MathF.Max(0.0f, finalSize.Width - (2.0f * margin)));
        float height = MathF.Min(desired.Height, MathF.Max(0.0f, finalSize.Height - (2.0f * margin)));
        float dpi = Root?.Dpi ?? UiDpi.Default;
        float left = UiDpi.SnapToPixel((finalSize.Width - width) / 2.0f, dpi);
        float top = UiDpi.SnapToPixel((finalSize.Height - height) / 2.0f, dpi);
        float right = UiDpi.SnapToPixel((finalSize.Width + width) / 2.0f, dpi);
        float bottom = UiDpi.SnapToPixel((finalSize.Height + height) / 2.0f, dpi);
        _surface.Arrange(new RectangleF(
            left,
            top,
            right - left,
            bottom - top));
    }

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        bool continues = _visibility.Update(context);
        if (!IsOpen && !continues)
        {
            _surface.SetContent(null);
            IsVisible = false;
        }

        return continues;
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        context.FillRoundedRectangle(
            new RoundedRectangle(new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height), 0.0f, 0.0f),
            new Color4(0.0f, 0.0f, 0.0f, BackdropAlpha),
            _visibility.Current);
    }

    internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
    {
        switch (input.Kind)
        {
            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                _backdropPressed = true;
                return new UiPointerResult(Consumed: true, CapturePointer: true);

            case WindowPointerEventKind.Released when _backdropPressed:
                _backdropPressed = false;
                if (!_surface.Bounds.Contains(input.Position) && Content?.DismissOnBackdrop == true)
                {
                    _dismiss!();
                }

                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Cancelled:
                _backdropPressed = false;
                return new UiPointerResult(Consumed: true);

            default:
                return new UiPointerResult(Consumed: true);
        }
    }

    private sealed class ModalSurface(ModalHost host) : UiElement
    {
        // The content being shown, which outlives the host's Content while it fades out.
        internal ModalContent? Content { get; private set; }
        internal override bool PreservesFocusOnPointerPress => true;
        internal override float Opacity => host._visibility.Current;
        internal override float VisualScale => ClosedScale + ((1.0f - ClosedScale) * host._visibility.Current);

        internal void SetContent(ModalContent? content)
        {
            if (Content is not null)
            {
                RemoveChild(Content);
            }

            Content = content;
            if (content is not null)
            {
                AddChild(content);
            }
        }

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            if (Content is null)
            {
                return SizeF.Empty;
            }

            var contentSize = new SizeF(
                MathF.Min(Content.PreferredSize.Width, availableSize.Width),
                MathF.Min(Content.PreferredSize.Height, availableSize.Height));
            Content.Measure(contentSize);
            return new SizeF(
                MathF.Min(Content.PreferredSize.Width, availableSize.Width),
                MathF.Min(Content.PreferredSize.Height, availableSize.Height));
        }

        protected override void ArrangeCore(SizeF finalSize)
        {
            if (Content is null)
            {
                return;
            }

            Content.Arrange(new RectangleF(PointF.Empty, finalSize));
        }

        protected override void DrawCore(in UiDrawContext context)
        {
            var panel = new RoundedRectangle(
                new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height),
                UiDesign.PanelCornerRadius,
                UiDesign.PanelCornerRadius);
            context.FillRoundedRectangle(panel, context.Palette.Surface);
            context.DrawRoundedRectangle(panel, context.Palette.SurfaceBorder);
        }

        internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
        {
            return new UiPointerResult(Consumed: true);
        }
    }
}
