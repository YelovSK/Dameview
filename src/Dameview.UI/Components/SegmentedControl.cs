using System.Drawing;
using Dameview.UI.Foundation;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

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
    private static readonly UiFont LabelFont = new(UiDesign.BodyFontSize, FontWeight.SemiBold, TextAlignment.Center);

    protected override void DrawCore(in UiDrawContext context)
    {
        var track = new RoundedRectangle(
            new RectangleF(PointF.Empty, Bounds.Size),
            UiDesign.ControlCornerRadius,
            UiDesign.ControlCornerRadius);
        context.FillRoundedRectangle(track, context.Palette.ControlSurface);
    }

    protected override void DrawSegment(
        in UiDrawContext context,
        RectangleF bounds,
        string label,
        bool selected,
        float hoverAmount,
        float pressedAmount)
    {
        var shape = new RoundedRectangle(bounds, SegmentCornerRadius, SegmentCornerRadius);
        if (selected)
        {
            context.FillRoundedRectangle(shape, context.Palette.Accent, 0.18f);
        }

        if (hoverAmount > 0.0f)
        {
            context.FillRoundedRectangle(shape, context.Palette.ControlHover, hoverAmount);
        }

        if (pressedAmount > 0.0f)
        {
            context.FillRoundedRectangle(shape, context.Palette.ControlPressed, pressedAmount);
        }

        context.DrawText(
            label,
            LabelFont,
            new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            selected ? context.Palette.PrimaryText : context.Palette.SecondaryText,
            DrawTextOptions.Clip);
    }
}
