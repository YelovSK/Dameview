using System.Drawing;
using Dameview.Platform;

namespace Dameview.UI.Layout;

internal sealed class ScrollView : UiElement
{
    private readonly UiElement _content;
    private float _scrollOffset;

    internal ScrollView(UiElement content)
    {
        _content = content;
        AddChild(content);
    }

    internal float ScrollOffset => _scrollOffset;
    internal override bool PreservesFocusOnPointerPress => true;

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        SizeF desired = _content.Measure(new SizeF(availableSize.Width, float.PositiveInfinity));
        float height = float.IsFinite(availableSize.Height)
            ? MathF.Min(desired.Height, MathF.Max(0.0f, availableSize.Height))
            : desired.Height;
        return new SizeF(MathF.Min(desired.Width, availableSize.Width), height);
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        ClampScroll(finalSize.Height);
        ArrangeContent(finalSize);
    }

    internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
    {
        if (input.Kind != UiPointerEventKind.Wheel)
        {
            return default;
        }

        float previous = _scrollOffset;
        _scrollOffset -= input.WheelDelta / 120.0f * 48.0f;
        ClampScroll(Bounds.Height);
        if (_scrollOffset != previous)
        {
            ArrangeContent(Bounds.Size);
        }

        return new UiPointerResult(Consumed: true, NeedsRepaint: _scrollOffset != previous);
    }

    internal override void BringIntoView(RectangleF descendantBounds)
    {
        float previous = _scrollOffset;
        if (descendantBounds.Top < 0.0f)
        {
            _scrollOffset += descendantBounds.Top;
        }
        else if (descendantBounds.Bottom > Bounds.Height)
        {
            _scrollOffset += descendantBounds.Bottom - Bounds.Height;
        }

        ClampScroll(Bounds.Height);
        if (_scrollOffset != previous)
        {
            ArrangeContent(Bounds.Size);
            InvalidateVisual();
        }
    }

    private void ArrangeContent(SizeF viewportSize)
    {
        _content.Arrange(new RectangleF(
            0.0f,
            -_scrollOffset,
            MathF.Max(0.0f, viewportSize.Width),
            MathF.Max(_content.DesiredSize.Height, viewportSize.Height)));
    }

    private void ClampScroll(float viewportHeight)
    {
        _scrollOffset = Math.Clamp(
            _scrollOffset,
            0.0f,
            MathF.Max(0.0f, _content.DesiredSize.Height - viewportHeight));
    }
}
