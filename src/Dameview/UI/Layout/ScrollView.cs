using System.Drawing;
using Dameview.Platform;

namespace Dameview.UI.Layout;

internal sealed class ScrollView : UiElement
{
    private const float ScrollbarWidth = 12.0f;
    private const float ScrollbarGap = 2.0f;
    private readonly UiElement _content;
    private readonly Scrollbar _scrollbar;
    private readonly ScrollOffsetController _scrollOffset = new();
    private float _contentHeight;
    private float _contentWidth;

    internal ScrollView(UiElement content)
    {
        _content = content;
        _scrollbar = new Scrollbar(SetScrollOffset);
        AddChild(content);
        AddChild(_scrollbar);
    }

    internal float ScrollOffset => _scrollOffset.Offset;
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
        _scrollOffset.SetMaximum(MathF.Max(0.0f, _contentHeight - finalSize.Height));
        _contentWidth = contentWidth;
        ArrangeContent(finalSize, contentWidth);
        _scrollbar.Arrange(new RectangleF(
            MathF.Max(0.0f, finalSize.Width - ScrollbarWidth),
            0.0f,
            ScrollbarWidth,
            finalSize.Height));
        _scrollbar.SetMetrics(_contentHeight, finalSize.Height, _scrollOffset.Offset);
    }

    internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
    {
        if (input.Kind != UiPointerEventKind.Wheel)
        {
            return default;
        }

        bool changed = _scrollOffset.ScrollBy(-input.WheelDelta / 120.0f * 48.0f);
        return new UiPointerResult(Consumed: true, NeedsRepaint: changed);
    }

    internal override void BringIntoView(RectangleF descendantBounds)
    {
        float target = _scrollOffset.TargetOffset;
        if (descendantBounds.Top < 0.0f)
        {
            target += descendantBounds.Top;
        }
        else if (descendantBounds.Bottom > Bounds.Height)
        {
            target += descendantBounds.Bottom - Bounds.Height;
        }

        if (_scrollOffset.SetImmediate(target))
        {
            ArrangeContent(Bounds.Size, _contentWidth);
            _scrollbar.SetMetrics(_contentHeight, Bounds.Height, _scrollOffset.Offset);
            InvalidateVisual();
        }
    }

    internal void SetScrollOffset(float offset)
    {
        if (_scrollOffset.SetImmediate(offset))
        {
            ArrangeContent(Bounds.Size, _contentWidth);
            _scrollbar.SetMetrics(_contentHeight, Bounds.Height, _scrollOffset.Offset);
            InvalidateVisual();
        }
    }

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        if (!_scrollOffset.Update(context.ElapsedSeconds))
        {
            return false;
        }

        ArrangeContent(Bounds.Size, _contentWidth);
        _scrollbar.SetMetrics(_contentHeight, Bounds.Height, _scrollOffset.Offset);
        return true;
    }

    private bool HasOverflow => MathF.Max(0.0f, _contentHeight - Bounds.Height) > 0.0f;
    private void ArrangeContent(SizeF viewportSize, float contentWidth)
    {
        _content.Arrange(new RectangleF(
            0.0f,
            -_scrollOffset.Offset,
            contentWidth,
            MathF.Max(_contentHeight, viewportSize.Height)));
    }
}
