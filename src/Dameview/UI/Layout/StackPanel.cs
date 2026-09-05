using System.Drawing;

namespace Dameview.UI.Layout;

internal enum UiOrientation
{
    Horizontal,
    Vertical,
}

internal enum StackPanelDistribution
{
    Natural,
    Equal,
}

internal sealed class StackPanel : UiElement
{
    private readonly UiOrientation _orientation;
    private readonly StackPanelDistribution _distribution;
    private readonly float _spacing;

    internal StackPanel(
        UiOrientation orientation,
        float spacing,
        StackPanelDistribution distribution,
        params UiElement[] children)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spacing);
        _orientation = orientation;
        _spacing = spacing;
        _distribution = distribution;
        foreach (UiElement child in children)
        {
            AddChild(child);
        }
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        UiElement[] visibleChildren = [.. Children.Where(child => child.IsVisible)];
        if (visibleChildren.Length == 0)
        {
            return SizeF.Empty;
        }

        float availableMain = Main(availableSize);
        float spacing = EffectiveSpacing(availableMain, visibleChildren.Length);
        float itemConstraint = _distribution == StackPanelDistribution.Equal
            && float.IsFinite(availableMain)
                ? MathF.Max(0.0f, (availableMain - spacing * (visibleChildren.Length - 1))
                    / visibleChildren.Length)
                : float.PositiveInfinity;
        float desiredMain = 0.0f;
        float desiredCross = 0.0f;
        foreach (UiElement child in visibleChildren)
        {
            SizeF desired = child.Measure(Size(itemConstraint, Cross(availableSize)));
            desiredMain = _distribution == StackPanelDistribution.Equal
                ? MathF.Max(desiredMain, Main(desired))
                : desiredMain + Main(desired);
            desiredCross = MathF.Max(desiredCross, Cross(desired));
        }

        if (_distribution == StackPanelDistribution.Equal)
        {
            desiredMain *= visibleChildren.Length;
        }

        desiredMain += spacing * (visibleChildren.Length - 1);
        return Size(desiredMain, desiredCross);
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        UiElement[] visibleChildren = [.. Children.Where(child => child.IsVisible)];
        if (visibleChildren.Length == 0)
        {
            return;
        }

        float finalMain = MathF.Max(0.0f, Main(finalSize));
        float finalCross = MathF.Max(0.0f, Cross(finalSize));
        float spacing = EffectiveSpacing(finalMain, visibleChildren.Length);
        float equalLength = MathF.Max(
            0.0f,
            (finalMain - spacing * (visibleChildren.Length - 1)) / visibleChildren.Length);
        float position = 0.0f;
        foreach (UiElement child in visibleChildren)
        {
            float length = _distribution == StackPanelDistribution.Equal
                ? equalLength
                : MathF.Max(0.0f, Main(child.DesiredSize));
            child.Arrange(CreateBounds(position, length, finalCross));
            position += length + spacing;
        }
    }

    protected override bool HitTestCore(PointF position) => false;

    private float Main(SizeF size) => _orientation == UiOrientation.Horizontal ? size.Width : size.Height;
    private float Cross(SizeF size) => _orientation == UiOrientation.Horizontal ? size.Height : size.Width;

    private SizeF Size(float main, float cross) => _orientation == UiOrientation.Horizontal
        ? new SizeF(main, cross)
        : new SizeF(cross, main);

    private RectangleF CreateBounds(float position, float length, float cross) =>
        _orientation == UiOrientation.Horizontal
            ? new RectangleF(position, 0.0f, length, cross)
            : new RectangleF(0.0f, position, cross, length);

    private float EffectiveSpacing(float availableMain, int childCount)
    {
        return childCount <= 1 || !float.IsFinite(availableMain)
            ? _spacing
            : MathF.Min(_spacing, MathF.Max(0.0f, availableMain) / (childCount - 1));
    }
}
