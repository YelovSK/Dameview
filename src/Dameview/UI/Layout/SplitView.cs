using System.Drawing;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;

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
    internal const float MinimumPaneSizeDips = UiDesign.MinimumPaneSize;
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
        _resizer.ResizeStarted += () => ResizeStarted?.Invoke();
        _resizer.ResizeCompleted += () => ResizeCompleted?.Invoke();
        // Collapsing slides the pane in from its edge while the first pane gives way.
        _secondPane.Transition = new UiTransition(Collapse: true);
        _secondPane.IsPresent = false;
        AddChild(firstPane);
        AddChild(secondPane);
        AddChild(_resizer);
    }

    /// <summary>Raised when the user begins dragging the splitter.</summary>
    internal event Action? ResizeStarted;
    /// <summary>Raised when the user finishes or cancels dragging the splitter.</summary>
    internal event Action? ResizeCompleted;

    internal SplitViewEdge Edge { get; private set; }
    internal RectangleF FirstPaneBounds => _firstPaneBounds;
    internal RectangleF SecondPaneBounds => _secondPaneBounds;
    internal float DividerOffsetDips { get; private set; }
    internal bool IsHorizontal => Edge is SplitViewEdge.Left or SplitViewEdge.Right;

    /// <summary>Whether the second pane is shown; changing it slides the pane in or out.</summary>
    internal bool SecondPaneVisible
    {
        get => _secondPane.IsPresent;
        set => _secondPane.IsPresent = value;
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
        if (!_secondPane.IsVisible)
        {
            _firstPaneBounds = new RectangleF(PointF.Empty, finalSize);
            _secondPaneBounds = RectangleF.Empty;
            return;
        }

        float margin = UiDesign.WindowMargin;
        float usableAxis = (IsHorizontal ? finalSize.Width : finalSize.Height)
            - 2.0f * margin
            - SplitterSize;
        float maximumSplitSize = MathF.Max(MinimumPaneSizeDips, usableAxis - MinimumPaneSizeDips);
        float splitSize = Math.Clamp(DividerOffsetDips, MinimumPaneSizeDips, maximumSplitSize);
        // How far from its edge the first pane ends. The second pane keeps its size and
        // sits just beyond that, so while collapsing it slides out past the window edge.
        float reserved = (margin + splitSize + SplitterSize) * _secondPane.LayoutPresence;
        float secondPaneStart = reserved - SplitterSize - splitSize;

        float secondPaneWidth = IsHorizontal ? splitSize : MathF.Max(0.0f, finalSize.Width - 2.0f * margin);
        float secondPaneHeight = IsHorizontal ? MathF.Max(0.0f, finalSize.Height - 2.0f * margin) : splitSize;
        switch (Edge)
        {
            case SplitViewEdge.Right:
                _secondPaneBounds = new RectangleF(finalSize.Width - secondPaneStart - secondPaneWidth, margin, secondPaneWidth, secondPaneHeight);
                _firstPaneBounds = new RectangleF(0.0f, 0.0f, MathF.Max(0.0f, finalSize.Width - reserved), finalSize.Height);
                break;

            case SplitViewEdge.Left:
                _secondPaneBounds = new RectangleF(secondPaneStart, margin, secondPaneWidth, secondPaneHeight);
                _firstPaneBounds = new RectangleF(reserved, 0.0f, MathF.Max(0.0f, finalSize.Width - reserved), finalSize.Height);
                break;

            case SplitViewEdge.Top:
                _secondPaneBounds = new RectangleF(margin, secondPaneStart, secondPaneWidth, secondPaneHeight);
                _firstPaneBounds = new RectangleF(0.0f, reserved, finalSize.Width, MathF.Max(0.0f, finalSize.Height - reserved));
                break;

            case SplitViewEdge.Bottom:
                _secondPaneBounds = new RectangleF(margin, finalSize.Height - secondPaneStart - secondPaneHeight, secondPaneWidth, secondPaneHeight);
                _firstPaneBounds = new RectangleF(0.0f, 0.0f, finalSize.Width, MathF.Max(0.0f, finalSize.Height - reserved));
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
