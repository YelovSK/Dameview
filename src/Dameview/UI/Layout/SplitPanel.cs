using System.Drawing;

namespace Dameview.UI.Layout;

// Symmetrically divides its bounds between two workspace children.
internal sealed class SplitPanel : UiElement, ISplitResizerTarget
{
    internal const float MinimumPaneSizeDips = 120.0f;
    internal const float SplitterSizeDips = 8.0f;

    private readonly UiElement _firstPane;
    private readonly UiElement _secondPane;
    private readonly UiOrientation _orientation;
    private readonly Action<float>? _ratioChanged;
    private readonly SplitResizer _resizer;
    private float _ratio;

    internal SplitPanel(
        UiElement firstPane,
        UiElement secondPane,
        UiOrientation orientation,
        float ratio,
        Action<float>? ratioChanged = null)
    {
        ArgumentNullException.ThrowIfNull(firstPane);
        ArgumentNullException.ThrowIfNull(secondPane);
        if (!(ratio > 0.0f && ratio < 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(ratio));
        }

        _firstPane = firstPane;
        _secondPane = secondPane;
        _orientation = orientation;
        _ratio = ratio;
        _ratioChanged = ratioChanged;
        _resizer = new SplitResizer(this);
        AddChild(firstPane);
        AddChild(secondPane);
        AddChild(_resizer);
    }

    internal RectangleF FirstPaneBounds { get; private set; }
    internal RectangleF SecondPaneBounds { get; private set; }

    internal (UiElement First, UiElement Second) DetachChildren()
    {
        RemoveChild(_firstPane);
        RemoveChild(_secondPane);
        RemoveChild(_resizer);
        return (_firstPane, _secondPane);
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        _firstPane.Measure(availableSize);
        _secondPane.Measure(availableSize);
        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        float mainLength = _orientation == UiOrientation.Horizontal
            ? finalSize.Width
            : finalSize.Height;
        float splitterSize = MathF.Min(SplitterSizeDips, MathF.Max(0.0f, mainLength));
        float usableLength = MathF.Max(0.0f, mainLength - splitterSize);
        float firstLength = CalculateFirstLength(usableLength);
        float secondLength = MathF.Max(0.0f, usableLength - firstLength);

        if (_orientation == UiOrientation.Horizontal)
        {
            FirstPaneBounds = new RectangleF(0.0f, 0.0f, firstLength, finalSize.Height);
            SecondPaneBounds = new RectangleF(
                firstLength + splitterSize,
                0.0f,
                secondLength,
                finalSize.Height);
        }
        else
        {
            FirstPaneBounds = new RectangleF(0.0f, 0.0f, finalSize.Width, firstLength);
            SecondPaneBounds = new RectangleF(
                0.0f,
                firstLength + splitterSize,
                finalSize.Width,
                secondLength);
        }

        _firstPane.Arrange(FirstPaneBounds);
        _secondPane.Arrange(SecondPaneBounds);
        _resizer.Arrange(GetResizerBounds(firstLength, splitterSize, finalSize));
    }

    protected override bool HitTestCore(PointF position) => false;

    private float CalculateFirstLength(float usableLength)
    {
        float requested = usableLength * _ratio;
        if (usableLength < 2.0f * MinimumPaneSizeDips)
        {
            return Math.Clamp(requested, 0.0f, usableLength);
        }

        return Math.Clamp(
            requested,
            MinimumPaneSizeDips,
            usableLength - MinimumPaneSizeDips);
    }

    UiOrientation ISplitResizerTarget.Orientation => _orientation;

    bool ISplitResizerTarget.CanResize
    {
        get
        {
            float mainLength = _orientation == UiOrientation.Horizontal
                ? Bounds.Width
                : Bounds.Height;
            return mainLength - SplitterSizeDips >= 2.0f * MinimumPaneSizeDips;
        }
    }

    float ISplitResizerTarget.DividerPosition
    {
        get => _orientation == UiOrientation.Horizontal
            ? FirstPaneBounds.Width
            : FirstPaneBounds.Height;
        set => SetFirstPaneLength(value);
    }

    private RectangleF GetResizerBounds(float firstLength, float splitterSize, SizeF finalSize)
    {
        return _orientation == UiOrientation.Horizontal
            ? new RectangleF(firstLength, 0.0f, splitterSize, finalSize.Height)
            : new RectangleF(0.0f, firstLength, finalSize.Width, splitterSize);
    }

    private void SetFirstPaneLength(float length)
    {
        float mainLength = _orientation == UiOrientation.Horizontal
            ? Bounds.Width
            : Bounds.Height;
        float usableLength = mainLength - SplitterSizeDips;
        if (usableLength < 2.0f * MinimumPaneSizeDips)
        {
            return;
        }

        float firstLength = Math.Clamp(
            length,
            MinimumPaneSizeDips,
            usableLength - MinimumPaneSizeDips);
        float ratio = firstLength / usableLength;
        if (ratio == _ratio)
        {
            return;
        }

        _ratioChanged?.Invoke(ratio);
        _ratio = ratio;
        InvalidateLayout();
    }
}
