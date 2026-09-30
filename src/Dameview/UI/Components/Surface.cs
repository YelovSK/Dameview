using System.Drawing;
using Dameview.UI.Foundation;
using Vortice.Direct2D1;

namespace Dameview.UI.Components;

internal enum UiSurfaceFill
{
    Surface,
    Overlay,
    None,
}

/// <summary>A rounded, bordered box around one child, whose margin pads it.</summary>
internal sealed class Surface : UiElement
{
    internal Surface(UiElement content) => AddChild(content);

    internal float CornerRadius { get; init; } = UiDesign.PanelCornerRadius;
    internal UiSurfaceFill Fill { get; init; } = UiSurfaceFill.Surface;

    protected override void DrawCore(in UiDrawContext context)
    {
        RoundedRectangle surface = new(new RectangleF(PointF.Empty, Bounds.Size), CornerRadius, CornerRadius);
        switch (Fill)
        {
            case UiSurfaceFill.Surface:
                context.FillRoundedRectangle(surface, context.Palette.Surface);
                break;

            case UiSurfaceFill.Overlay:
                context.FillRoundedRectangle(surface, context.Palette.OverlaySurface);
                break;
        }

        context.DrawRoundedRectangle(surface, context.Palette.SurfaceBorder);
    }
}
