using Vortice.Mathematics;
using Color = System.Drawing.Color;

namespace Dameview.UI.Foundation;

internal sealed record UiTheme(
    Color4 Background,
    Color4 Surface,
    Color4 OverlaySurface,
    Color4 SurfaceBorder,
    Color4 ControlSurface,
    Color4 Accent,
    Color4 ControlHover,
    Color4 ControlPressed,
    Color4 PrimaryText,
    Color4 SecondaryText,
    Color4 ErrorText,
    Color4 WarningText,
    Color4 SuccessText)
{
    internal Color WindowCaptionColor => ToWindowColor(Background);
    internal Color WindowTextColor => ToWindowColor(PrimaryText);

    private static Color ToWindowColor(Color4 color) => Color.FromArgb(
        (byte)MathF.Round(color.R * 255f),
        (byte)MathF.Round(color.G * 255f),
        (byte)MathF.Round(color.B * 255f));
}
