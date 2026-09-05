using System.Drawing;
using Dameview.Platform;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Dameview.UI;

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
    private readonly Action _dismiss;
    private readonly ModalSurface _surface;
    private ModalContent? _content;
    private bool _backdropPressed;

    internal ModalHost(Action dismiss)
    {
        _dismiss = dismiss;
        _surface = new ModalSurface();
        AddChild(_surface);
        IsVisible = false;
    }

    internal bool IsOpen => _content is not null;
    internal override bool PreservesFocusOnPointerPress => true;

    internal void Show(ModalContent content)
    {
        Root?.ClearPointer();
        _surface.SetContent(content);
        _content = content;
        _backdropPressed = false;
        IsVisible = true;
    }

    internal void Close()
    {
        Root?.ClearPointer();
        _surface.SetContent(null);
        _content = null;
        _backdropPressed = false;
        IsVisible = false;
    }

    internal bool HandleEscape()
    {
        if (_content is null)
        {
            return false;
        }

        if (_content.DismissOnEscape)
        {
            _dismiss();
        }
        else
        {
            _content.OnKeyEvent(new UiKeyEvent(UiKey.Escape));
        }

        return true;
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        if (_content is not null)
        {
            _surface.Measure(availableSize);
        }

        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        if (_content is null)
        {
            return;
        }

        const float margin = 12.0f;
        SizeF desired = _content.PreferredSize;
        float width = MathF.Min(desired.Width, MathF.Max(0.0f, finalSize.Width - (2.0f * margin)));
        float height = MathF.Min(desired.Height, MathF.Max(0.0f, finalSize.Height - (2.0f * margin)));
        _surface.Arrange(new RectangleF(
            (finalSize.Width - width) / 2.0f,
            (finalSize.Height - height) / 2.0f,
            width,
            height));
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        context.FillRoundedRectangle(
            new RoundedRectangle(new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height), 0.0f, 0.0f),
            new Color4(0.0f, 0.0f, 0.0f, 0.45f));
    }

    internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
    {
        switch (input.Kind)
        {
            case UiPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                _backdropPressed = true;
                return new UiPointerResult(Consumed: true, CapturePointer: true);

            case UiPointerEventKind.Released when _backdropPressed:
                _backdropPressed = false;
                if (!_surface.Bounds.Contains(input.Position) && _content?.DismissOnBackdrop == true)
                {
                    _dismiss();
                }

                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case UiPointerEventKind.Cancelled:
                _backdropPressed = false;
                return new UiPointerResult(Consumed: true);

            default:
                return new UiPointerResult(Consumed: true);
        }
    }

    private sealed class ModalSurface : UiElement
    {
        private ModalContent? _content;
        private float _scroll;

        internal override bool PreservesFocusOnPointerPress => true;

        internal void SetContent(ModalContent? content)
        {
            if (_content is not null)
            {
                RemoveChild(_content);
            }

            _content = content;
            _scroll = 0.0f;
            if (content is not null)
            {
                AddChild(content);
            }
        }

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            if (_content is null)
            {
                return SizeF.Empty;
            }

            _content.Measure(_content.PreferredSize);
            return new SizeF(
                MathF.Min(_content.PreferredSize.Width, availableSize.Width),
                MathF.Min(_content.PreferredSize.Height, availableSize.Height));
        }

        protected override void ArrangeCore(SizeF finalSize)
        {
            if (_content is null)
            {
                return;
            }

            ClampScroll(finalSize.Height);
            _content.Arrange(new RectangleF(
                0.0f,
                -_scroll,
                finalSize.Width,
                _content.PreferredSize.Height));
        }

        protected override void DrawCore(in UiDrawContext context)
        {
            UiDesignTokens design = context.Palette.Design;
            var panel = new RoundedRectangle(
                new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height),
                design.PanelCornerRadius,
                design.PanelCornerRadius);
            context.FillRoundedRectangle(panel, context.Palette.Surface);
            context.DrawRoundedRectangle(panel, context.Palette.SurfaceBorder);
        }

        internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
        {
            if (input.Kind != UiPointerEventKind.Wheel || _content is null)
            {
                return new UiPointerResult(Consumed: true);
            }

            float previous = _scroll;
            _scroll -= input.WheelDelta / 120.0f * 48.0f;
            ClampScroll(Bounds.Height);
            if (_scroll != previous)
            {
                ArrangeCore(Bounds.Size);
            }

            return new UiPointerResult(Consumed: true, NeedsRepaint: _scroll != previous);
        }

        internal override void BringIntoView(RectangleF descendantBounds)
        {
            float previous = _scroll;
            if (descendantBounds.Top < 0.0f)
            {
                _scroll += descendantBounds.Top;
            }
            else if (descendantBounds.Bottom > Bounds.Height)
            {
                _scroll += descendantBounds.Bottom - Bounds.Height;
            }

            ClampScroll(Bounds.Height);
            if (_scroll != previous)
            {
                ArrangeCore(Bounds.Size);
                InvalidateVisual();
            }
        }

        private void ClampScroll(float viewportHeight)
        {
            float contentHeight = _content?.PreferredSize.Height ?? 0.0f;
            _scroll = Math.Clamp(_scroll, 0.0f, MathF.Max(0.0f, contentHeight - viewportHeight));
        }
    }
}
