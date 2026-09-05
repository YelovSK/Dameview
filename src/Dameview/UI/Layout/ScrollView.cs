using System.Drawing;
using Dameview.Platform;

namespace Dameview.UI.Layout;

internal sealed class ScrollView : UiElement
{
    private const float ScrollbarWidth = 12.0f;
    private const float ScrollbarGap = 2.0f;
    private readonly UiElement _content;
    private readonly Scrollbar _scrollbar;
    private float _scrollOffset;
    private float _contentHeight;

    internal ScrollView(UiElement content)
    {
        _content = content;
        _scrollbar = new Scrollbar(SetScrollOffset);
        AddChild(content);
        AddChild(_scrollbar);
    }

    internal float ScrollOffset => _scrollOffset;
    internal bool IsScrollbarVisible => HasOverflow;
    internal RectangleF ScrollbarThumbBounds => _scrollbar.ThumbBounds;
    internal override bool PreservesFocusOnPointerPress => true;

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        _content.Measure(new SizeF(availableSize.Width, float.PositiveInfinity));
        return new SizeF(
            MathF.Min(_content.DesiredSize.Width, availableSize.Width),
            MathF.Max(0.0f, availableSize.Height));
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        _content.Measure(new SizeF(finalSize.Width, float.PositiveInfinity));
        bool reserveScrollbar = _content.DesiredSize.Height > finalSize.Height;
        float contentWidth = reserveScrollbar
            ? MathF.Max(0.0f, finalSize.Width - ScrollbarWidth - ScrollbarGap)
            : finalSize.Width;
        if (reserveScrollbar)
        {
            _content.Measure(new SizeF(contentWidth, float.PositiveInfinity));
        }

        _contentHeight = MathF.Max(0.0f, _content.DesiredSize.Height);
        ClampScroll(finalSize.Height);
        ArrangeContent(finalSize, contentWidth);
        _scrollbar.Arrange(new RectangleF(
            MathF.Max(0.0f, finalSize.Width - ScrollbarWidth),
            0.0f,
            ScrollbarWidth,
            finalSize.Height));
        _scrollbar.SetMetrics(_contentHeight, finalSize.Height, _scrollOffset);
    }

    internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
    {
        if (input.Kind != UiPointerEventKind.Wheel)
        {
            return default;
        }

        float previous = _scrollOffset;
        SetScrollOffset(_scrollOffset - input.WheelDelta / 120.0f * 48.0f);
        return new UiPointerResult(Consumed: true, NeedsRepaint: _scrollOffset != previous);
    }

    internal override void BringIntoView(RectangleF descendantBounds)
    {
        float previous = _scrollOffset;
        if (descendantBounds.Top < 0.0f)
        {
            SetScrollOffset(_scrollOffset + descendantBounds.Top);
        }
        else if (descendantBounds.Bottom > Bounds.Height)
        {
            SetScrollOffset(_scrollOffset + descendantBounds.Bottom - Bounds.Height);
        }

        if (_scrollOffset != previous)
        {
            InvalidateVisual();
        }
    }

    internal void SetScrollOffset(float offset)
    {
        float previous = _scrollOffset;
        _scrollOffset = Math.Clamp(offset, 0.0f, MaximumScrollOffset);
        if (_scrollOffset != previous)
        {
            ArrangeContent(Bounds.Size, MathF.Max(0.0f, Bounds.Width - ScrollbarWidth - ScrollbarGap));
            _scrollbar.SetMetrics(_contentHeight, Bounds.Height, _scrollOffset);
            InvalidateVisual();
        }
    }

    private bool HasOverflow => MaximumScrollOffset > 0.0f;
    private float MaximumScrollOffset => MathF.Max(0.0f, _contentHeight - Bounds.Height);

    private void ArrangeContent(SizeF viewportSize, float contentWidth)
    {
        _content.Arrange(new RectangleF(
            0.0f,
            -_scrollOffset,
            contentWidth,
            MathF.Max(_contentHeight, viewportSize.Height)));
    }

    private void ClampScroll(float viewportHeight)
    {
        _scrollOffset = Math.Clamp(
            _scrollOffset,
            0.0f,
            MathF.Max(0.0f, _contentHeight - viewportHeight));
    }

}
