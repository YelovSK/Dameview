using System.Drawing;
using Dameview.UI.Foundation;
using Vortice.Direct2D1;

namespace Dameview.UI.Components;

/// <summary>Picks one of a few values, all shown side by side in a shared track.</summary>
internal sealed class SegmentedControl<T>(
    IReadOnlyList<Choice<T>> choices,
    T selectedValue,
    Action<T> changed)
    : ChoiceStrip<T>(choices, selectedValue, changed, segmentHeight: 30.0f, spacing: TrackPadding, padding: TrackPadding)
{
    private const float TrackPadding = 3.0f;
    private const float SegmentCornerRadius = UiDesign.ControlCornerRadius - TrackPadding;

    protected override void DrawBackground(in UiDrawContext context, RectangleF selection)
    {
        var track = new RoundedRectangle(
            new RectangleF(PointF.Empty, Bounds.Size),
            UiDesign.ControlCornerRadius,
            UiDesign.ControlCornerRadius);
        context.FillRoundedRectangle(track, context.Palette.ControlSurface);
        context.FillRoundedRectangle(
            new RoundedRectangle(selection, SegmentCornerRadius, SegmentCornerRadius),
            context.Palette.Accent,
            0.18f);
    }
}
