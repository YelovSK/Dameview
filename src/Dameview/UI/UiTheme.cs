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
    internal static UiTheme Light { get; } = new(
        Background: FromHex("#EBEDF2"),
        Surface: FromHex("#FCFCFF"),
        OverlaySurface: FromHex("#FAFAFF", alpha: 0.94f),
        SurfaceBorder: FromHex("#B8BFCC"),
        Accent: FromHex("#1F5CBF"),
        ControlHover: FromHex("#D6DEEB"),
        ControlPressed: FromHex("#BAC9E0"),
        PrimaryText: FromHex("#171C26"),
        SecondaryText: FromHex("#525C6B"),
        ErrorText: FromHex("#BF1A1F"));

    internal static UiTheme Default { get; } = new(
        Background: FromHex("#090A0C"),
        Surface: FromHex("#121418"),
        OverlaySurface: FromHex("#0E1013", alpha: 0.94f),
        SurfaceBorder: FromHex("#292E38"),
        Accent: FromHex("#599EFF"),
        ControlHover: FromHex("#292E38"),
        ControlPressed: FromHex("#3D4759"),
        PrimaryText: FromHex("#F0F2F7"),
        SecondaryText: FromHex("#919CAD"),
        ErrorText: FromHex("#FF6E73"));

    internal static UiTheme CatppuccinFrappe { get; } = new(
        Background: FromHex("#232634"),
        Surface: FromHex("#303446"),
        OverlaySurface: FromHex("#292C3C", alpha: 0.94f),
        SurfaceBorder: FromHex("#414559"),
        Accent: FromHex("#CA9EE6"),
        ControlHover: FromHex("#51576D"),
        ControlPressed: FromHex("#626880"),
        PrimaryText: FromHex("#C6D0F5"),
        SecondaryText: FromHex("#A5ADCE"),
        ErrorText: FromHex("#E78284"));

    internal static UiTheme CatppuccinMacchiato { get; } = new(
        Background: FromHex("#181926"),
        Surface: FromHex("#24273A"),
        OverlaySurface: FromHex("#1E2030", alpha: 0.94f),
        SurfaceBorder: FromHex("#363A4F"),
        Accent: FromHex("#C6A0F6"),
        ControlHover: FromHex("#494D64"),
        ControlPressed: FromHex("#5B6078"),
        PrimaryText: FromHex("#CAD3F5"),
        SecondaryText: FromHex("#A5ADCB"),
        ErrorText: FromHex("#ED8796"));

    internal static UiTheme CatppuccinMocha { get; } = new(
        Background: FromHex("#11111B"),
        Surface: FromHex("#1E1E2E"),
        OverlaySurface: FromHex("#181825", alpha: 0.94f),
        SurfaceBorder: FromHex("#313244"),
        Accent: FromHex("#CBA6F7"),
        ControlHover: FromHex("#45475A"),
        ControlPressed: FromHex("#585B70"),
        PrimaryText: FromHex("#CDD6F4"),
        SecondaryText: FromHex("#A6ADC8"),
        ErrorText: FromHex("#F38BA8"));

    internal static UiTheme GruvboxDark { get; } = new(
        Background: FromHex("#282828"),
        Surface: FromHex("#3C3836"),
        OverlaySurface: FromHex("#3C3836", alpha: 0.94f),
        SurfaceBorder: FromHex("#504945"),
        Accent: FromHex("#83A598"),
        ControlHover: FromHex("#504945"),
        ControlPressed: FromHex("#665C54"),
        PrimaryText: FromHex("#EBDBB2"),
        SecondaryText: FromHex("#BDAE93"),
        ErrorText: FromHex("#FB4934"));

    internal static UiTheme Nord { get; } = new(
        Background: FromHex("#2E3440"),
        Surface: FromHex("#3B4252"),
        OverlaySurface: FromHex("#3B4252", alpha: 0.94f),
        SurfaceBorder: FromHex("#4C566A"),
        Accent: FromHex("#88C0D0"),
        ControlHover: FromHex("#434C5E"),
        ControlPressed: FromHex("#4C566A"),
        PrimaryText: FromHex("#ECEFF4"),
        SecondaryText: FromHex("#D8DEE9"),
        ErrorText: FromHex("#BF616A"));

    internal static UiTheme Dracula { get; } = new(
        Background: FromHex("#282A36"),
        Surface: FromHex("#44475A"),
        OverlaySurface: FromHex("#44475A", alpha: 0.94f),
        SurfaceBorder: FromHex("#6272A4"),
        Accent: FromHex("#BD93F9"),
        ControlHover: FromHex("#44475A"),
        ControlPressed: FromHex("#6272A4"),
        PrimaryText: FromHex("#F8F8F2"),
        SecondaryText: FromHex("#6272A4"),
        ErrorText: FromHex("#FF5555"));

    internal static UiTheme RosePine { get; } = new(
        Background: FromHex("#191724"),
        Surface: FromHex("#1F1D2E"),
        OverlaySurface: FromHex("#26233A", alpha: 0.94f),
        SurfaceBorder: FromHex("#403D52"),
        Accent: FromHex("#C4A7E7"),
        ControlHover: FromHex("#26233A"),
        ControlPressed: FromHex("#403D52"),
        PrimaryText: FromHex("#E0DEF4"),
        SecondaryText: FromHex("#6E6A86"),
        ErrorText: FromHex("#EB6F92"));

    private static Color4 FromHex(string hex, float alpha = 1.0f)
    {
        ReadOnlySpan<char> digits = hex.AsSpan(hex[0] == '#' ? 1 : 0);
        byte r = (byte)Convert.ToInt32(digits[..2].ToString(), 16);
        byte g = (byte)Convert.ToInt32(digits[2..4].ToString(), 16);
        byte b = (byte)Convert.ToInt32(digits[4..6].ToString(), 16);
        return new Color4(r / 255f, g / 255f, b / 255f, alpha);
    }
}

/// <summary>A named theme option: a persisted identity, its display name, and its palette.</summary>
internal sealed record Theme(string Id, string DisplayName, UiTheme Palette, bool IsDark);

/// <summary>The catalog of available themes.</summary>
internal static class Themes
{
    internal static Theme Dark { get; } = new("dark", "Dark", UiTheme.Default, IsDark: true);
    internal static Theme Light { get; } = new("light", "Light", UiTheme.Light, IsDark: false);
    internal static Theme CatppuccinFrappe { get; } = new(
        "catppuccinFrappe",
        "Catppuccin Frappé",
        UiTheme.CatppuccinFrappe,
        IsDark: true);
    internal static Theme CatppuccinMacchiato { get; } = new(
        "catppuccinMacchiato",
        "Catppuccin Macchiato",
        UiTheme.CatppuccinMacchiato,
        IsDark: true);
    internal static Theme CatppuccinMocha { get; } = new(
        "catppuccinMocha",
        "Catppuccin Mocha",
        UiTheme.CatppuccinMocha,
        IsDark: true);
    internal static Theme GruvboxDark { get; } = new(
        "gruvboxDark",
        "Gruvbox Dark",
        UiTheme.GruvboxDark,
        IsDark: true);
    internal static Theme Nord { get; } = new(
        "nord",
        "Nord",
        UiTheme.Nord,
        IsDark: true);
    internal static Theme Dracula { get; } = new(
        "dracula",
        "Dracula",
        UiTheme.Dracula,
        IsDark: true);
    internal static Theme RosePine { get; } = new(
        "rosePine",
        "Rosé Pine",
        UiTheme.RosePine,
        IsDark: true);

    internal static IReadOnlyList<Theme> All { get; } =
    [
        Dark,
        Light,
        CatppuccinFrappe,
        CatppuccinMacchiato,
        CatppuccinMocha,
        GruvboxDark,
        Nord,
        Dracula,
        RosePine,
    ];

    internal static Theme? FromId(string id) => id switch
    {
        "dark" => Dark,
        "light" => Light,
        "catppuccinFrappe" => CatppuccinFrappe,
        "catppuccinMacchiato" => CatppuccinMacchiato,
        "catppuccinMocha" => CatppuccinMocha,
        "gruvboxDark" => GruvboxDark,
        "nord" => Nord,
        "dracula" => Dracula,
        "rosePine" => RosePine,
        _ => null,
    };
}
