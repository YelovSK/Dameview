using System.Globalization;
using Dameview.Navigation;
using Dameview.Serialization;

namespace Dameview.Settings;

internal static class SettingsIniSerializer
{
    internal static AppSettings Read(string text)
    {
        var document = IniDocument.Parse(text);
        ThemeId theme = ReadTheme(document.Get(string.Empty, "theme"));
        bool animationsEnabled = ReadOptionalBoolean(
            document.Get(string.Empty, "animations"),
            defaultValue: true);
        FolderSort sort = ReadSort(document.Get(string.Empty, "sort"));
        GalleryThumbnailSize galleryThumbnailSize = ReadGalleryThumbnailSize(
            document.Get(string.Empty, "galleryThumbnailSize"));
        float galleryWidth = ReadOptionalFloat(
            document.Get(string.Empty, "galleryWidth"),
            AppSettings.DefaultGalleryWidthDips);
        WindowPlacementState? window = !document.HasSection("window") ? null : new WindowPlacementState
        {
            X = ReadRequiredInt(document.Get("window", "x")),
            Y = ReadRequiredInt(document.Get("window", "y")),
            Width = ReadRequiredInt(document.Get("window", "width")),
            Height = ReadRequiredInt(document.Get("window", "height")),
            Maximized = ReadRequiredBoolean(document.Get("window", "maximized")),
        };

        return new AppSettings
        {
            Theme = theme,
            AnimationsEnabled = animationsEnabled,
            Sort = sort,
            GalleryThumbnailSize = galleryThumbnailSize,
            GalleryWidthDips = galleryWidth,
            Window = window,
        };
    }

    internal static string Write(AppSettings settings)
    {
        var document = new IniDocument();
        document.Set(string.Empty, "theme", WriteTheme(settings.Theme));
        document.Set(string.Empty, "animations", settings.AnimationsEnabled ? "true" : "false");
        document.Set(string.Empty, "sort", WriteSort(settings.Sort));
        document.Set(string.Empty, "galleryThumbnailSize", WriteGalleryThumbnailSize(settings.GalleryThumbnailSize));
        document.Set(string.Empty, "galleryWidth", settings.GalleryWidthDips.ToString(CultureInfo.InvariantCulture));
        if (settings.Window is { } window)
        {
            document.Set("window", "x", window.X.ToString(CultureInfo.InvariantCulture));
            document.Set("window", "y", window.Y.ToString(CultureInfo.InvariantCulture));
            document.Set("window", "width", window.Width.ToString(CultureInfo.InvariantCulture));
            document.Set("window", "height", window.Height.ToString(CultureInfo.InvariantCulture));
            document.Set("window", "maximized", window.Maximized ? "true" : "false");
        }

        return document.Write();
    }

    private static ThemeId ReadTheme(string? value) => value switch
    {
        null or "dark" => ThemeId.Dark,
        "light" => ThemeId.Light,
        "catppuccinFrappe" => ThemeId.CatppuccinFrappe,
        "catppuccinMacchiato" => ThemeId.CatppuccinMacchiato,
        "catppuccinMocha" => ThemeId.CatppuccinMocha,
        "gruvboxDark" => ThemeId.GruvboxDark,
        "nord" => ThemeId.Nord,
        "dracula" => ThemeId.Dracula,
        "rosePine" => ThemeId.RosePine,
        _ => throw new IniFormatException("Unknown theme value."),
    };

    private static FolderSort ReadSort(string? value) => value switch
    {
        null => FolderSort.NameAscending,
        "nameAscending" => FolderSort.NameAscending,
        "nameDescending" => FolderSort.NameDescending,
        "dateModifiedNewest" => FolderSort.DateModifiedNewest,
        "dateModifiedOldest" => FolderSort.DateModifiedOldest,
        "dateCreatedNewest" => FolderSort.DateCreatedNewest,
        "dateCreatedOldest" => FolderSort.DateCreatedOldest,
        "sizeLargest" => FolderSort.SizeLargest,
        "sizeSmallest" => FolderSort.SizeSmallest,
        _ => throw new IniFormatException("Unknown sort value."),
    };

    private static GalleryThumbnailSize ReadGalleryThumbnailSize(string? value) => value switch
    {
        null => GalleryThumbnailSize.Medium,
        "small" => GalleryThumbnailSize.Small,
        "medium" => GalleryThumbnailSize.Medium,
        "large" => GalleryThumbnailSize.Large,
        _ => throw new IniFormatException("Unknown gallery thumbnail size."),
    };

    private static int ReadRequiredInt(string? value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
        {
            return result;
        }

        throw new IniFormatException("Expected an integer.");
    }

    private static bool ReadRequiredBoolean(string? value) => value switch
    {
        "true" => true,
        "false" => false,
        _ => throw new IniFormatException("Expected true or false."),
    };

    private static bool ReadOptionalBoolean(string? value, bool defaultValue) =>
        value is null ? defaultValue : ReadRequiredBoolean(value);

    private static float ReadOptionalFloat(string? value, float defaultValue)
    {
        if (value is null)
        {
            return defaultValue;
        }

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
        {
            return result;
        }

        throw new IniFormatException("Expected a number.");
    }

    private static string WriteTheme(ThemeId value) => value switch
    {
        ThemeId.Dark => "dark",
        ThemeId.Light => "light",
        ThemeId.CatppuccinFrappe => "catppuccinFrappe",
        ThemeId.CatppuccinMacchiato => "catppuccinMacchiato",
        ThemeId.CatppuccinMocha => "catppuccinMocha",
        ThemeId.GruvboxDark => "gruvboxDark",
        ThemeId.Nord => "nord",
        ThemeId.Dracula => "dracula",
        ThemeId.RosePine => "rosePine",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string WriteGalleryThumbnailSize(GalleryThumbnailSize value) => value switch
    {
        GalleryThumbnailSize.Small => "small",
        GalleryThumbnailSize.Medium => "medium",
        GalleryThumbnailSize.Large => "large",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string WriteSort(FolderSort value) => value switch
    {
        FolderSort.NameAscending => "nameAscending",
        FolderSort.NameDescending => "nameDescending",
        FolderSort.DateModifiedNewest => "dateModifiedNewest",
        FolderSort.DateModifiedOldest => "dateModifiedOldest",
        FolderSort.DateCreatedNewest => "dateCreatedNewest",
        FolderSort.DateCreatedOldest => "dateCreatedOldest",
        FolderSort.SizeLargest => "sizeLargest",
        FolderSort.SizeSmallest => "sizeSmallest",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
