using System.Drawing;

namespace Dameview.UI.Layout;

internal sealed class Overlay : UiElement
{
    internal Overlay(params UiElement[] children)
    {
        foreach (UiElement child in children)
        {
            AddChild(child);
        }
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float width = 0.0f;
        float height = 0.0f;
        foreach (UiElement child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            SizeF desired = child.Measure(availableSize);
            width = MathF.Max(width, desired.Width);
            height = MathF.Max(height, desired.Height);
        }

        return new SizeF(width, height);
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        var bounds = new RectangleF(PointF.Empty, finalSize);
        foreach (UiElement child in Children)
        {
            if (child.IsVisible)
            {
                child.Arrange(bounds);
            }
        }
    }

    protected override bool HitTestCore(PointF position) => false;
}
