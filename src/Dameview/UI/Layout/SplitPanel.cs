using System.Drawing;

namespace Dameview.UI.Layout;

// Symmetrically divides its bounds between two workspace children.
internal sealed class SplitPanel : UiElement
{
    internal const float MinimumPaneSizeDips = 120.0f;
    internal const float SplitterSizeDips = 8.0f;

    private readonly UiElement _firstPane;
    private readonly UiElement _secondPane;
    private readonly UiOrientation _orientation;
    private readonly float _ratio;

    internal SplitPanel(
        UiElement firstPane,
        UiElement secondPane,
        UiOrientation orientation,
        float ratio)
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
        AddChild(firstPane);
        AddChild(secondPane);
    }

    internal RectangleF FirstPaneBounds { get; private set; }
    internal RectangleF SecondPaneBounds { get; private set; }

    internal (UiElement First, UiElement Second) DetachChildren()
    {
        RemoveChild(_firstPane);
        RemoveChild(_secondPane);
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
}
