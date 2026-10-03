using System.Drawing;
using Dameview.UI.Foundation;
using Vortice.Direct2D1;

namespace Dameview.UI.Components;

/// <summary>Switches between the pages of a panel.</summary>
internal sealed class TabStrip<T>(
    IReadOnlyList<Choice<T>> choices,
    T selectedValue,
    Action<T> changed)
    : ChoiceStrip<T>(choices, selectedValue, changed, segmentHeight: 36.0f, spacing: UiDesign.SmallSpacing, padding: 0.0f)
{
    protected override void DrawBackground(in UiDrawContext context, RectangleF selection) =>
        context.FillRoundedRectangle(
            new RoundedRectangle(selection, UiDesign.ControlCornerRadius, UiDesign.ControlCornerRadius),
            context.Palette.Accent,
            0.18f);
}
