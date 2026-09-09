using System.Drawing;

namespace Dameview.UI.Layout;

internal enum SplitViewEdge
{
    Right,
    Left,
    Top,
    Bottom,
}

internal sealed class SplitView : UiElement, ISplitResizerTarget
{
    internal const float MinimumPaneSizeDips = 120.0f;
    private const float SplitterSize = 8.0f;

    private readonly UiElement _firstPane;
    private readonly UiElement _secondPane;
    private readonly SplitResizer _resizer;
    private RectangleF _firstPaneBounds;
    private RectangleF _secondPaneBounds;

    internal SplitView(
        UiElement firstPane,
        UiElement secondPane,
        float initialDividerOffsetDips,
        SplitViewEdge edge = SplitViewEdge.Right)
    {
        _firstPane = firstPane;
        _secondPane = secondPane;
        DividerOffsetDips = initialDividerOffsetDips;
        Edge = edge;
        _resizer = new SplitResizer(this);
        _secondPane.IsVisible = false;
        AddChild(firstPane);
        AddChild(secondPane);
        AddChild(_resizer);
    }

    internal SplitViewEdge Edge { get; private set; }
    internal RectangleF FirstPaneBounds => _firstPaneBounds;
    internal RectangleF SecondPaneBounds => _secondPaneBounds;
    internal float DividerOffsetDips { get; private set; }
    internal bool IsHorizontal => Edge is SplitViewEdge.Left or SplitViewEdge.Right;

    internal bool FirstPaneVisible
    {
        get; set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            _firstPane.IsVisible = value;
            InvalidateLayout();
        }
    } = true;

    internal bool SecondPaneVisible
    {
        get; set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            _secondPane.IsVisible = value;
            InvalidateLayout();
        }
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        _firstPane.Measure(availableSize);
        _secondPane.Measure(availableSize);
        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        CalculateBounds(finalSize);
        _firstPane.Arrange(_firstPaneBounds);
        _secondPane.Arrange(_secondPaneBounds);
        _resizer.Arrange(GetResizerBounds());
    }

    internal void SetDividerOffset(float offset)
    {
        float previous = DividerOffsetDips;
        DividerOffsetDips = MathF.Max(offset, MinimumPaneSizeDips);
        if (DividerOffsetDips != previous)
        {
            InvalidateLayout();
        }
    }

    internal void SetEdge(SplitViewEdge edge)
    {
        if (Edge == edge)
        {
            return;
        }

        Edge = edge;
        InvalidateLayout();
    }

    private void CalculateBounds(SizeF finalSize)
    {
        float margin = UiDesign.WindowMargin;
        float usableAxis = (IsHorizontal ? finalSize.Width : finalSize.Height)
            - 2.0f * margin
            - SplitterSize;
        float maximumSplitSize = MathF.Max(MinimumPaneSizeDips, usableAxis - MinimumPaneSizeDips);
        float splitSize = Math.Clamp(DividerOffsetDips, MinimumPaneSizeDips, maximumSplitSize);

        if (!FirstPaneVisible && !SecondPaneVisible)
        {
            _firstPaneBounds = RectangleF.Empty;
            _secondPaneBounds = RectangleF.Empty;
            return;
        }

        if (!SecondPaneVisible || splitSize <= 0.0f)
        {
            _firstPaneBounds = FirstPaneVisible
                ? new RectangleF(0.0f, 0.0f, finalSize.Width, finalSize.Height)
                : RectangleF.Empty;
            _secondPaneBounds = RectangleF.Empty;
            return;
        }

        if (!FirstPaneVisible)
        {
            _firstPaneBounds = RectangleF.Empty;
            _secondPaneBounds = new RectangleF(0.0f, 0.0f, finalSize.Width, finalSize.Height);
            return;
        }

        float secondPaneWidth = IsHorizontal ? splitSize : MathF.Max(0.0f, finalSize.Width - 2.0f * margin);
        float secondPaneHeight = IsHorizontal ? MathF.Max(0.0f, finalSize.Height - 2.0f * margin) : splitSize;
        switch (Edge)
        {
            case SplitViewEdge.Right:
                _secondPaneBounds = new RectangleF(finalSize.Width - margin - secondPaneWidth, margin, secondPaneWidth, secondPaneHeight);
                _firstPaneBounds = new RectangleF(0.0f, 0.0f, _secondPaneBounds.X - SplitterSize, finalSize.Height);
                break;

            case SplitViewEdge.Left:
                _secondPaneBounds = new RectangleF(margin, margin, secondPaneWidth, secondPaneHeight);
                _firstPaneBounds = new RectangleF(
                    _secondPaneBounds.Right + SplitterSize,
                    0.0f,
                    MathF.Max(0.0f, finalSize.Width - _secondPaneBounds.Right - SplitterSize),
                    finalSize.Height);
                break;

            case SplitViewEdge.Top:
                _secondPaneBounds = new RectangleF(margin, margin, secondPaneWidth, secondPaneHeight);
                _firstPaneBounds = new RectangleF(
                    0.0f,
                    _secondPaneBounds.Bottom + SplitterSize,
                    finalSize.Width,
                    MathF.Max(0.0f, finalSize.Height - _secondPaneBounds.Bottom - SplitterSize));
                break;

            case SplitViewEdge.Bottom:
                _secondPaneBounds = new RectangleF(margin, finalSize.Height - margin - secondPaneHeight, secondPaneWidth, secondPaneHeight);
                _firstPaneBounds = new RectangleF(
                    0.0f,
                    0.0f,
                    finalSize.Width,
                    MathF.Max(0.0f, _secondPaneBounds.Y - SplitterSize));
                break;

            default:
                throw new InvalidOperationException($"Unsupported split edge: {Edge}.");
        }
    }

    private RectangleF GetResizerBounds()
    {
        if (_secondPaneBounds == RectangleF.Empty)
        {
            return RectangleF.Empty;
        }

        return Edge switch
        {
            SplitViewEdge.Right => new RectangleF(
                _secondPaneBounds.X - SplitterSize, _secondPaneBounds.Y, SplitterSize, _secondPaneBounds.Height),
            SplitViewEdge.Left => new RectangleF(
                _secondPaneBounds.Right, _secondPaneBounds.Y, SplitterSize, _secondPaneBounds.Height),
            SplitViewEdge.Top => new RectangleF(
                _secondPaneBounds.X, _secondPaneBounds.Bottom, _secondPaneBounds.Width, SplitterSize),
            SplitViewEdge.Bottom => new RectangleF(
                _secondPaneBounds.X, _secondPaneBounds.Y - SplitterSize, _secondPaneBounds.Width, SplitterSize),
            _ => RectangleF.Empty,
        };
    }

    UiOrientation ISplitResizerTarget.Orientation => IsHorizontal
        ? UiOrientation.Horizontal
        : UiOrientation.Vertical;

    bool ISplitResizerTarget.CanResize => SecondPaneVisible && SecondPaneBounds != RectangleF.Empty;

    float ISplitResizerTarget.DividerPosition
    {
        get => Edge is SplitViewEdge.Left or SplitViewEdge.Top
            ? DividerOffsetDips
            : -DividerOffsetDips;
        set => SetDividerOffset(Edge is SplitViewEdge.Left or SplitViewEdge.Top ? value : -value);
    }
}
