using System.Drawing;
using Dameview.UI.Foundation;

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

    internal StackPanel(UiOrientation orientation, params UiElement[] children)
    {
        _orientation = orientation;
        foreach (UiElement child in children)
        {
            AddChild(child);
        }
    }

    /// <summary>The gap between neighboring children, which shrinks when a stack is too short for it.</summary>
    internal float Spacing
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    internal StackPanelDistribution Distribution { get; init; } = StackPanelDistribution.Natural;

    /// <summary>The child that takes whatever length the others leave, instead of its own, in a natural stack.</summary>
    internal UiElement? Fill { get; init; }

    internal void Add(UiElement child) => AddChild(child);

    internal void Remove(UiElement child) => RemoveChild(child);

    internal void Reorder(IReadOnlyList<UiElement> children) => SetChildOrder(children);

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        UiElement[] visibleChildren = [.. Children.Where(child => child.IsVisible)];
        if (visibleChildren.Length == 0)
        {
            return SizeF.Empty;
        }

        float availableMain = Main(availableSize);
        float spacing = EffectiveSpacing(availableMain, visibleChildren.Length);
        float itemConstraint = Distribution == StackPanelDistribution.Equal
            && float.IsFinite(availableMain)
                ? MathF.Max(0.0f, (availableMain - spacing * (visibleChildren.Length - 1))
                    / visibleChildren.Length)
                : float.PositiveInfinity;
        float desiredMain = 0.0f;
        float desiredCross = 0.0f;
        foreach (UiElement child in visibleChildren)
        {
            if (child == Fill)
            {
                continue;
            }

            SizeF desired = child.Measure(Size(itemConstraint, Cross(availableSize)));
            desiredMain = Distribution == StackPanelDistribution.Equal
                ? MathF.Max(desiredMain, Main(desired))
                : desiredMain + (Main(desired) * GetLayoutPresence(child));
            desiredCross = MathF.Max(desiredCross, Cross(desired));
        }

        if (Distribution == StackPanelDistribution.Equal)
        {
            desiredMain *= visibleChildren.Length;
        }

        desiredMain += GetGapsAfter(visibleChildren, spacing).Sum();
        if (Fill is { IsVisible: true } fill)
        {
            SizeF desired = fill.Measure(Size(MathF.Max(0.0f, availableMain - desiredMain), Cross(availableSize)));
            desiredMain += Main(desired);
            desiredCross = MathF.Max(desiredCross, Cross(desired));
        }

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
        float[] gaps = GetGapsAfter(visibleChildren, spacing);
        float[] lengths = new float[visibleChildren.Length];
        float fillLength = finalMain;
        for (int index = 0; index < visibleChildren.Length; index++)
        {
            UiElement child = visibleChildren[index];
            if (child != Fill)
            {
                lengths[index] = Distribution == StackPanelDistribution.Equal
                    ? equalLength
                    : MathF.Max(0.0f, Main(child.DesiredSize)) * GetLayoutPresence(child);
                fillLength -= lengths[index];
            }

            fillLength -= gaps[index];
        }

        float position = 0.0f;
        for (int index = 0; index < visibleChildren.Length; index++)
        {
            UiElement child = visibleChildren[index];
            float length = child == Fill ? MathF.Max(0.0f, fillLength) : lengths[index];
            child.Arrange(CreateBounds(position, length, finalCross));
            position += length + gaps[index];
        }
    }

    // A collapsing child gives up its slot. Equal slots cannot shrink one child.
    private float GetLayoutPresence(UiElement child) =>
        Distribution == StackPanelDistribution.Natural ? child.LayoutPresence : 1.0f;

    // A gap shrinks with the child before it and with whatever still follows it, so a collapsing
    // child takes one gap with it and the stack always ends exactly at its last child.
    private float[] GetGapsAfter(UiElement[] children, float spacing)
    {
        float[] gaps = new float[children.Length];
        float laterPresence = 0.0f;
        for (int index = children.Length - 1; index >= 0; index--)
        {
            float presence = GetLayoutPresence(children[index]);
            gaps[index] = spacing * presence * laterPresence;
            laterPresence = MathF.Max(laterPresence, presence);
        }

        return gaps;
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
            ? Spacing
            : MathF.Min(Spacing, MathF.Max(0.0f, availableMain) / (childCount - 1));
    }
}
