using Vortice.Mathematics;

namespace Dameview.UI;

internal sealed record UiTheme(
    Color4 Background,
    Color4 Surface,
    Color4 OverlaySurface,
    Color4 SurfaceBorder,
    Color4 Accent,
    Color4 PrimaryText,
    Color4 SecondaryText,
    Color4 ErrorText)
{
    internal static UiTheme Default { get; } = new(
        Background: new Color4(0.035f, 0.039f, 0.047f, 1.0f),
        Surface: new Color4(0.070f, 0.078f, 0.094f, 1.0f),
        OverlaySurface: new Color4(0.055f, 0.061f, 0.074f, 0.94f),
        SurfaceBorder: new Color4(0.16f, 0.18f, 0.22f, 1.0f),
        Accent: new Color4(0.35f, 0.62f, 1.0f, 1.0f),
        PrimaryText: new Color4(0.94f, 0.95f, 0.97f, 1.0f),
        SecondaryText: new Color4(0.57f, 0.61f, 0.68f, 1.0f),
        ErrorText: new Color4(1.0f, 0.43f, 0.45f, 1.0f));
}
