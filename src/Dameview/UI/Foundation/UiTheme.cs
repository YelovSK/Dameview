using Dameview.Settings;
using Vortice.Mathematics;

namespace Dameview.UI;

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
    Color4 ErrorText)
{
    internal static UiTheme Light { get; } = new(
        Background: FromHex("#EBEDF2"),
        Surface: FromHex("#FCFCFF"),
        OverlaySurface: FromHex("#FAFAFF", alpha: 0.94f),
        SurfaceBorder: FromHex("#B8BFCC"),
        ControlSurface: FromHex("#EBEDF2"),
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
        ControlSurface: FromHex("#1C2027"),
        Accent: FromHex("#599EFF"),
        ControlHover: FromHex("#292E38"),
        ControlPressed: FromHex("#3D4759"),
        PrimaryText: FromHex("#F0F2F7"),
        SecondaryText: FromHex("#919CAD"),
        ErrorText: FromHex("#FF6E73"));

    // Source: https://github.com/catppuccin/palette/blob/main/palette.json
    internal static UiTheme CatppuccinFrappe { get; } = new(
        Background: FromHex("#232634"), // Crust
        Surface: FromHex("#303446"), // Base
        OverlaySurface: FromHex("#292C3C", alpha: 0.94f), // Mantle
        SurfaceBorder: FromHex("#414559"), // Surface0
        ControlSurface: FromHex("#414559"), // Surface0
        Accent: FromHex("#CA9EE6"), // Mauve
        ControlHover: FromHex("#51576D"), // Surface1
        ControlPressed: FromHex("#626880"), // Surface2
        PrimaryText: FromHex("#C6D0F5"), // Text
        SecondaryText: FromHex("#A5ADCE"), // Subtext0
        ErrorText: FromHex("#E78284")); // Red

    internal static UiTheme CatppuccinMacchiato { get; } = new(
        Background: FromHex("#181926"), // Crust
        Surface: FromHex("#24273A"), // Base
        OverlaySurface: FromHex("#1E2030", alpha: 0.94f), // Mantle
        SurfaceBorder: FromHex("#363A4F"), // Surface0
        ControlSurface: FromHex("#363A4F"), // Surface0
        Accent: FromHex("#C6A0F6"), // Mauve
        ControlHover: FromHex("#494D64"), // Surface1
        ControlPressed: FromHex("#5B6078"), // Surface2
        PrimaryText: FromHex("#CAD3F5"), // Text
        SecondaryText: FromHex("#A5ADCB"), // Subtext0
        ErrorText: FromHex("#ED8796")); // Red

    internal static UiTheme CatppuccinMocha { get; } = new(
        Background: FromHex("#11111B"), // Crust
        Surface: FromHex("#1E1E2E"), // Base
        OverlaySurface: FromHex("#181825", alpha: 0.94f), // Mantle
        SurfaceBorder: FromHex("#313244"), // Surface0
        ControlSurface: FromHex("#313244"), // Surface0
        Accent: FromHex("#CBA6F7"), // Mauve
        ControlHover: FromHex("#45475A"), // Surface1
        ControlPressed: FromHex("#585B70"), // Surface2
        PrimaryText: FromHex("#CDD6F4"), // Text
        SecondaryText: FromHex("#A6ADC8"), // Subtext0
        ErrorText: FromHex("#F38BA8")); // Red

    // Source: https://github.com/morhetz/gruvbox/blob/master/colors/gruvbox.vim
    internal static UiTheme GruvboxDark { get; } = new(
        Background: FromHex("#282828"), // dark0
        Surface: FromHex("#3C3836"), // dark1
        OverlaySurface: FromHex("#3C3836", alpha: 0.94f), // dark1
        SurfaceBorder: FromHex("#504945"), // dark2
        ControlSurface: FromHex("#504945"), // dark2
        Accent: FromHex("#83A598"), // bright_blue
        ControlHover: FromHex("#665C54"), // dark3
        ControlPressed: FromHex("#7C6F64"), // dark4
        PrimaryText: FromHex("#EBDBB2"), // light1
        SecondaryText: FromHex("#BDAE93"), // light3
        ErrorText: FromHex("#FB4934")); // bright_red

    // Source: https://www.nordtheme.com/docs/colors-and-palettes
    internal static UiTheme Nord { get; } = new(
        Background: FromHex("#2E3440"), // nord0
        Surface: FromHex("#3B4252"), // nord1
        OverlaySurface: FromHex("#3B4252", alpha: 0.94f), // nord1
        SurfaceBorder: FromHex("#4C566A"), // nord3
        ControlSurface: FromHex("#434C5E"), // nord2
        Accent: FromHex("#88C0D0"), // nord8
        ControlHover: FromHex("#4C566A"), // nord3
        ControlPressed: FromHex("#596579"), // Dameview interaction shade beyond Polar Night
        PrimaryText: FromHex("#ECEFF4"), // nord6
        SecondaryText: FromHex("#D8DEE9"), // nord4
        ErrorText: FromHex("#BF616A")); // nord11

    // Source: https://github.com/dracula/draculatheme.com/blob/main/content/spec.mdx
    internal static UiTheme Dracula { get; } = new(
        Background: FromHex("#282A36"), // Background
        Surface: FromHex("#44475A"), // Selection
        OverlaySurface: FromHex("#44475A", alpha: 0.94f), // Selection
        SurfaceBorder: FromHex("#6272A4"), // Current Line / Comment
        ControlSurface: FromHex("#353747"), // Official opaque Current Line fallback
        Accent: FromHex("#BD93F9"), // Purple
        ControlHover: FromHex("#44475A"), // Selection
        ControlPressed: FromHex("#6272A4"), // Current Line / Comment
        PrimaryText: FromHex("#F8F8F2"), // Foreground
        SecondaryText: FromHex("#6272A4"), // Comment
        ErrorText: FromHex("#FF5555")); // Red

    // Source: https://github.com/rose-pine/rose-pine-palette/blob/main/palette.json
    internal static UiTheme RosePine { get; } = new(
        Background: FromHex("#191724"), // Base
        Surface: FromHex("#1F1D2E"), // Surface
        OverlaySurface: FromHex("#26233A", alpha: 0.94f), // Overlay
        SurfaceBorder: FromHex("#403D52"), // Highlight Medium
        ControlSurface: FromHex("#26233A"), // Overlay
        Accent: FromHex("#C4A7E7"), // Iris
        ControlHover: FromHex("#403D52"), // Highlight Medium
        ControlPressed: FromHex("#524F67"), // Highlight High
        PrimaryText: FromHex("#E0DEF4"), // Text
        SecondaryText: FromHex("#6E6A86"), // Muted
        ErrorText: FromHex("#EB6F92")); // Love

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
internal sealed record Theme(ThemeId Id, string DisplayName, UiTheme Palette, bool IsDark);

/// <summary>The catalog of available themes.</summary>
internal static class Themes
{
    internal static Theme Dark { get; } = new(ThemeId.Dark, "Dark", UiTheme.Default, IsDark: true);
    internal static Theme Light { get; } = new(ThemeId.Light, "Light", UiTheme.Light, IsDark: false);
    internal static Theme CatppuccinFrappe { get; } = new(
        ThemeId.CatppuccinFrappe,
        "Catppuccin Frappé",
        UiTheme.CatppuccinFrappe,
        IsDark: true);
    internal static Theme CatppuccinMacchiato { get; } = new(
        ThemeId.CatppuccinMacchiato,
        "Catppuccin Macchiato",
        UiTheme.CatppuccinMacchiato,
        IsDark: true);
    internal static Theme CatppuccinMocha { get; } = new(
        ThemeId.CatppuccinMocha,
        "Catppuccin Mocha",
        UiTheme.CatppuccinMocha,
        IsDark: true);
    internal static Theme GruvboxDark { get; } = new(
        ThemeId.GruvboxDark,
        "Gruvbox Dark",
        UiTheme.GruvboxDark,
        IsDark: true);
    internal static Theme Nord { get; } = new(
        ThemeId.Nord,
        "Nord",
        UiTheme.Nord,
        IsDark: true);
    internal static Theme Dracula { get; } = new(
        ThemeId.Dracula,
        "Dracula",
        UiTheme.Dracula,
        IsDark: true);
    internal static Theme RosePine { get; } = new(
        ThemeId.RosePine,
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

    internal static Theme Get(ThemeId id) => id switch
    {
        ThemeId.Dark => Dark,
        ThemeId.Light => Light,
        ThemeId.CatppuccinFrappe => CatppuccinFrappe,
        ThemeId.CatppuccinMacchiato => CatppuccinMacchiato,
        ThemeId.CatppuccinMocha => CatppuccinMocha,
        ThemeId.GruvboxDark => GruvboxDark,
        ThemeId.Nord => Nord,
        ThemeId.Dracula => Dracula,
        ThemeId.RosePine => RosePine,
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };
}
