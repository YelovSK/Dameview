using Vortice.Mathematics;

namespace Dameview.UI;

internal sealed record UiTheme(
    Color4 Background,
    Color4 Surface,
    Color4 OverlaySurface,
    Color4 SurfaceBorder,
    Color4 Accent,
    Color4 ControlHover,
    Color4 ControlPressed,
    Color4 PrimaryText,
    Color4 SecondaryText,
    Color4 ErrorText)
{
    internal UiDesignTokens Design { get; init; } = UiDesignTokens.Default;

    internal static UiTheme Light { get; } = new(
        Background: new Color4(0.92f, 0.93f, 0.95f, 1.0f),
        Surface: new Color4(0.99f, 0.99f, 1.0f, 1.0f),
        OverlaySurface: new Color4(0.98f, 0.98f, 1.0f, 0.94f),
        SurfaceBorder: new Color4(0.72f, 0.75f, 0.80f, 1.0f),
        Accent: new Color4(0.12f, 0.36f, 0.75f, 1.0f),
        ControlHover: new Color4(0.84f, 0.87f, 0.92f, 1.0f),
        ControlPressed: new Color4(0.73f, 0.79f, 0.88f, 1.0f),
        PrimaryText: new Color4(0.09f, 0.11f, 0.15f, 1.0f),
        SecondaryText: new Color4(0.32f, 0.36f, 0.42f, 1.0f),
        ErrorText: new Color4(0.75f, 0.10f, 0.12f, 1.0f));

    internal static UiTheme Default { get; } = new(
        Background: new Color4(0.035f, 0.039f, 0.047f, 1.0f),
        Surface: new Color4(0.070f, 0.078f, 0.094f, 1.0f),
        OverlaySurface: new Color4(0.055f, 0.061f, 0.074f, 0.94f),
        SurfaceBorder: new Color4(0.16f, 0.18f, 0.22f, 1.0f),
        Accent: new Color4(0.35f, 0.62f, 1.0f, 1.0f),
        ControlHover: new Color4(0.16f, 0.18f, 0.22f, 1.0f),
        ControlPressed: new Color4(0.24f, 0.28f, 0.35f, 1.0f),
        PrimaryText: new Color4(0.94f, 0.95f, 0.97f, 1.0f),
        SecondaryText: new Color4(0.57f, 0.61f, 0.68f, 1.0f),
        ErrorText: new Color4(1.0f, 0.43f, 0.45f, 1.0f));
}

internal sealed record UiDesignTokens(
    float WindowMargin,
    float PanelGap,
    float StatusHeight,
    float ToolbarHeight,
    float ControlCornerRadius,
    float PanelCornerRadius,
    float SmallSpacing,
    float Spacing,
    float LargeSpacing,
    float BodyFontSize,
    float HeadingFontSize,
    double HoverResponse,
    double PressedResponse)
{
    internal static UiDesignTokens Default { get; } = new(
        WindowMargin: 12.0f,
        PanelGap: 8.0f,
        StatusHeight: 42.0f,
        ToolbarHeight: 46.0f,
        ControlCornerRadius: 8.0f,
        PanelCornerRadius: 12.0f,
        SmallSpacing: 4.0f,
        Spacing: 8.0f,
        LargeSpacing: 16.0f,
        BodyFontSize: 14.0f,
        HeadingFontSize: 22.0f,
        HoverResponse: 24.0,
        PressedResponse: 32.0);
}
