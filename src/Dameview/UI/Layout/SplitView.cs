using System.Drawing;
using Dameview.Platform;
using Vortice.Direct2D1;

namespace Dameview.UI.Layout;

internal enum SplitViewEdge
{
    Right,
    Left,
    Top,
    Bottom,
}

internal sealed class SplitView : UiElement
{
    internal const float MinimumPaneSizeDips = 120.0f;
    internal const float MaximumPaneSizeDips = 480.0f;
    private const float SplitterSize = 8.0f;

    private readonly UiElement _firstPane;
    private readonly UiElement _secondPane;
    private readonly Resizer _resizer;
    private readonly UiDesignTokens _design;
    private float _splitSize;
    private bool _firstPaneVisible = true;
    private bool _secondPaneVisible;
    private RectangleF _firstPaneBounds;
    private RectangleF _secondPaneBounds;

    internal SplitView(
        UiElement firstPane,
        UiElement secondPane,
        UiDesignTokens design,
        float initialSplitSizeDips,
        SplitViewEdge edge = SplitViewEdge.Right)
    {
        _firstPane = firstPane;
        _secondPane = secondPane;
        _design = design;
        _splitSize = initialSplitSizeDips;
        Edge = edge;
        _resizer = new Resizer(this);
        _secondPane.IsVisible = false;
        AddChild(firstPane);
        AddChild(secondPane);
        AddChild(_resizer);
    }

    internal SplitViewEdge Edge { get; private set; }
    internal RectangleF FirstPaneBounds => _firstPaneBounds;
    internal RectangleF SecondPaneBounds => _secondPaneBounds;
    internal float SplitSize => _splitSize;
    internal bool IsHorizontal => Edge is SplitViewEdge.Left or SplitViewEdge.Right;

    internal bool FirstPaneVisible
    {
        get => _firstPaneVisible;
        set
        {
            if (_firstPaneVisible == value)
            {
                return;
            }

            _firstPaneVisible = value;
            _firstPane.IsVisible = value;
            InvalidateLayout();
        }
    }

    internal bool SecondPaneVisible
    {
        get => _secondPaneVisible;
        set
        {
            if (_secondPaneVisible == value)
            {
                return;
            }

            _secondPaneVisible = value;
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

    internal void SetSplitSize(float size)
    {
        float previous = _splitSize;
        _splitSize = Math.Clamp(size, MinimumPaneSizeDips, MaximumPaneSizeDips);
        if (_splitSize != previous)
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
        float margin = _design.WindowMargin;
        float availableSize = IsHorizontal
            ? finalSize.Width - 2.0f * margin - SplitterSize - MinimumPaneSizeDips
            : finalSize.Height - 2.0f * margin - SplitterSize - MinimumPaneSizeDips;
        float splitSize = MathF.Min(_splitSize, MathF.Max(0.0f, availableSize));

        if (!_firstPaneVisible && !_secondPaneVisible)
        {
            _firstPaneBounds = RectangleF.Empty;
            _secondPaneBounds = RectangleF.Empty;
            return;
        }

        if (!_secondPaneVisible || splitSize <= 0.0f)
        {
            _firstPaneBounds = _firstPaneVisible
                ? new RectangleF(0.0f, 0.0f, finalSize.Width, finalSize.Height)
                : RectangleF.Empty;
            _secondPaneBounds = RectangleF.Empty;
            return;
        }

        if (!_firstPaneVisible)
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

    private float GetPointerCoordinate(Resizer resizer, PointF localPosition)
    {
        return IsHorizontal
            ? resizer.Bounds.X + localPosition.X
            : resizer.Bounds.Y + localPosition.Y;
    }

    private void SetSplitSizeFromPointer(float pointer, float dragStartPointer, float dragStartSize)
    {
        float delta = pointer - dragStartPointer;
        bool growsWithPointer = Edge is SplitViewEdge.Left or SplitViewEdge.Top;
        SetSplitSize(dragStartSize + (growsWithPointer ? delta : -delta));
    }

    private sealed class Resizer(SplitView owner) : UiElement
    {
        private bool _dragging;
        private float _dragStartPointer;
        private float _dragStartSize;

        internal override bool IsHitTestVisible => owner.SecondPaneVisible && owner.SecondPaneBounds != RectangleF.Empty;
        internal override bool PreservesFocusOnPointerPress => true;

        protected override void DrawCore(in UiDrawContext context)
        {
            if (!IsHitTestVisible)
            {
                return;
            }

            if (owner.IsHorizontal)
            {
                float center = Bounds.Width / 2.0f;
                context.FillRoundedRectangle(
                    new RoundedRectangle(
                        new RectangleF(center - 1.0f, 12.0f, 2.0f, MathF.Max(0.0f, Bounds.Height - 24.0f)),
                        1.0f,
                        1.0f),
                    _dragging ? context.Palette.Accent : context.Palette.SurfaceBorder,
                    _dragging ? 1.0f : 0.65f);
            }
            else
            {
                float center = Bounds.Height / 2.0f;
                context.FillRoundedRectangle(
                    new RoundedRectangle(
                        new RectangleF(12.0f, center - 1.0f, MathF.Max(0.0f, Bounds.Width - 24.0f), 2.0f),
                        1.0f,
                        1.0f),
                    _dragging ? context.Palette.Accent : context.Palette.SurfaceBorder,
                    _dragging ? 1.0f : 0.65f);
            }
        }

        internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
        {
            switch (input.Kind)
            {
                case UiPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                    _dragging = true;
                    _dragStartPointer = owner.GetPointerCoordinate(this, input.Position);
                    _dragStartSize = owner.SplitSize;
                    return new UiPointerResult(Consumed: true, CapturePointer: true, NeedsRepaint: true);

                case UiPointerEventKind.Moved when _dragging:
                    float pointer = owner.GetPointerCoordinate(this, input.Position);
                    owner.SetSplitSizeFromPointer(pointer, _dragStartPointer, _dragStartSize);
                    return new UiPointerResult(Consumed: true, NeedsRepaint: true);

                case UiPointerEventKind.Released when _dragging:
                    _dragging = false;
                    return new UiPointerResult(Consumed: true, NeedsRepaint: true);

                case UiPointerEventKind.Cancelled when _dragging:
                    _dragging = false;
                    return new UiPointerResult(Consumed: true, NeedsRepaint: true);

                default:
                    return default;
            }
        }
    }
}
