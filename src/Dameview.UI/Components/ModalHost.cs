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
    private const float BackdropAlpha = 0.45f;
    private const float ClosedScale = 0.96f;

    private readonly ModalSurface _surface;
    private Action? _dismiss;
    private bool _backdropPressed;

    internal ModalHost()
    {
        _surface = new ModalSurface(this);
        AddChild(_surface);
        Transition = new UiTransition(Fade: true);
        IsPresent = false;
    }

    internal bool IsOpen => Content is not null;
    internal ModalContent? Content { get; private set; }
    internal override bool PreservesFocusOnPointerPress => true;

    internal void Show(ModalContent content, Action dismiss)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(dismiss);
        Root?.ClearPointer();
        _surface.SetContent(content);
        Content = content;
        _dismiss = dismiss;
        _backdropPressed = false;
        IsPresent = true;
    }

    internal void Close()
    {
        if (Content is null)
        {
            return;
        }

        Root?.ClearPointer();
        Content = null;
        _dismiss = null;
        _backdropPressed = false;
        IsPresent = false;
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

    protected override void DrawCore(in UiDrawContext context)
    {
        context.FillRoundedRectangle(
            new RoundedRectangle(new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height), 0.0f, 0.0f),
            new Color4(0.0f, 0.0f, 0.0f, BackdropAlpha));
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
        // The content last shown, which stays after the host closes so that it can fade out.
        internal ModalContent? Content { get; private set; }
        internal override bool PreservesFocusOnPointerPress => true;
        // The host fades everything; the panel also grows into place as it does.
        internal override float VisualScale => ClosedScale + ((1.0f - ClosedScale) * host.Presence);

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
