using System.Globalization;
using Dameview.Commands;
using Dameview.Diagnostics;
using Dameview.Navigation;
using Dameview.Serialization;
using Dameview.Win32;

namespace Dameview.Settings;

internal static class SettingsIniSerializer
{
    private const int MinimumWindowWidth = 320;
    private const int MinimumWindowHeight = 240;

    /// <returns>The settings, and every value that was unreadable and fell back to its default.</returns>
    internal static (AppSettings Settings, IReadOnlyList<string> Ignored) Read(string text)
    {
        var document = IniDocument.Parse(text);
        List<string> ignored = [];
        ThemeId theme = ReadTheme(document.Get(string.Empty, "theme"), ignored);
        bool animationsEnabled = ReadOptionalBoolean(
            document.Get(string.Empty, "animations"),
            defaultValue: true,
            ignored);
        bool singleInstance = ReadOptionalBoolean(
            document.Get(string.Empty, "singleInstance"),
            defaultValue: true,
            ignored);
        bool autoBalancePanes = ReadOptionalBoolean(
            document.Get(string.Empty, "autoBalancePanes"),
            defaultValue: false,
            ignored);
        FolderSort sort = ReadSort(document.Get(string.Empty, "sort"), ignored);
        bool galleryEnabled = ReadOptionalBoolean(
            document.Get(string.Empty, "galleryEnabled"),
            defaultValue: true,
            ignored);
        GalleryPlacement galleryPlacement = ReadGalleryPlacement(
            document.Get(string.Empty, "galleryPlacement"),
            ignored);
        GalleryThumbnailSize galleryThumbnailSize = ReadGalleryThumbnailSize(
            document.Get(string.Empty, "galleryThumbnailSize"),
            ignored);
        float gallerySize = ReadOptionalFloat(
            document.Get(string.Empty, "gallerySize"),
            AppSettings.DefaultGallerySizeDips,
            AppSettings.MinimumGallerySizeDips,
            ignored);
        WindowPlacementState? window = ReadWindow(document, ignored);
        LogLevel logLevel = ReadLogLevel(document.Get("logging", "level"), ignored);
        ViewerKeyBindings keyBindings = ReadKeyBindings(document, ignored);

        AppSettings settings = new()
        {
            Theme = theme,
            AnimationsEnabled = animationsEnabled,
            SingleInstance = singleInstance,
            AutoBalancePanes = autoBalancePanes,
            Sort = sort,
            GalleryEnabled = galleryEnabled,
            GalleryPlacement = galleryPlacement,
            GalleryThumbnailSize = galleryThumbnailSize,
            GallerySizeDips = gallerySize,
            Window = window,
            Logging = new LoggingSettings { Level = logLevel },
            KeyBindings = keyBindings,
        };
        return (settings, ignored);
    }

    internal static string Write(AppSettings settings)
    {
        var document = new IniDocument();
        document.Set(string.Empty, "theme", WriteTheme(settings.Theme));
        document.Set(string.Empty, "animations", settings.AnimationsEnabled ? "true" : "false");
        document.Set(string.Empty, "singleInstance", settings.SingleInstance ? "true" : "false");
        document.Set(
            string.Empty,
            "autoBalancePanes",
            settings.AutoBalancePanes ? "true" : "false");
        document.Set(string.Empty, "sort", WriteSort(settings.Sort));
        document.Set(string.Empty, "galleryEnabled", settings.GalleryEnabled ? "true" : "false");
        document.Set(string.Empty, "galleryPlacement", WriteGalleryPlacement(settings.GalleryPlacement));
        document.Set(string.Empty, "galleryThumbnailSize", WriteGalleryThumbnailSize(settings.GalleryThumbnailSize));
        document.Set(string.Empty, "gallerySize", settings.GallerySizeDips.ToString(CultureInfo.InvariantCulture));
        if (settings.Window is { } window)
        {
            document.Set("window", "x", window.X.ToString(CultureInfo.InvariantCulture));
            document.Set("window", "y", window.Y.ToString(CultureInfo.InvariantCulture));
            document.Set("window", "width", window.Width.ToString(CultureInfo.InvariantCulture));
            document.Set("window", "height", window.Height.ToString(CultureInfo.InvariantCulture));
            document.Set("window", "maximized", window.Maximized ? "true" : "false");
        }

        document.Set("logging", "level", WriteLogLevel(settings.Logging.Level));
        WriteKeyBindings(document, settings.KeyBindings);

        return document.Write();
    }

    // The placement only means anything complete, so any unreadable part discards it.
    private static WindowPlacementState? ReadWindow(IniDocument document, List<string> ignored)
    {
        if (!document.HasSection("window"))
        {
            return null;
        }

        if (!TryReadInt(document.Get("window", "x"), out int x)
            || !TryReadInt(document.Get("window", "y"), out int y)
            || !TryReadInt(document.Get("window", "width"), out int width)
            || !TryReadInt(document.Get("window", "height"), out int height))
        {
            Log.Warning("Settings", "Ignored an incomplete window placement.");
            ignored.Add("the window placement");
            return null;
        }

        var placement = new WindowPlacementState
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Maximized = ReadOptionalBoolean(document.Get("window", "maximized"), defaultValue: false, ignored),
        };
        return placement.Width >= MinimumWindowWidth && placement.Height >= MinimumWindowHeight
            ? placement
            : Fallback<WindowPlacementState?>("window size", $"{width}x{height}", null, ignored);
    }

    private static bool TryReadInt(string? value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static ViewerKeyBindings ReadKeyBindings(IniDocument document, List<string> ignored)
    {
        ViewerKeyBindings bindings = ViewerKeyBindings.Defaults;
        foreach (ViewerCommand command in ViewerCommandCatalog.Commands)
        {
            if (document.Get("keybindings", GetCommandKey(command.Id)) is string value)
            {
                bindings = bindings.WithShortcuts(command.Id, ReadShortcuts(value, ignored));
            }
        }

        return bindings;
    }

    private static ViewerCommandShortcut[] ReadShortcuts(string value, List<string> ignored)
    {
        var shortcuts = new List<ViewerCommandShortcut>();
        foreach (string part in value.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ViewerCommandShortcut.TryParse(part, out ViewerCommandShortcut shortcut))
            {
                shortcuts.Add(shortcut);
            }
            else
            {
                Log.Warning("Settings", $"Ignored unknown shortcut '{part}'.");
                ignored.Add($"shortcut '{part}'");
            }
        }

        return [.. shortcuts];
    }

    private static void WriteKeyBindings(IniDocument document, ViewerKeyBindings bindings)
    {
        foreach (ViewerCommand command in ViewerCommandCatalog.Commands)
        {
            document.Set(
                "keybindings",
                GetCommandKey(command.Id),
                string.Join(' ', bindings.GetShortcuts(command.Id).Select(shortcut => shortcut.Text)));
        }
    }

    private static string GetCommandKey(ViewerCommandId command)
    {
        string name = command.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static ThemeId ReadTheme(string? value, List<string> ignored) => value switch
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
        _ => Fallback("theme", value, ThemeId.Dark, ignored),
    };

    private static FolderSort ReadSort(string? value, List<string> ignored) => value switch
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
        _ => Fallback("sort", value, FolderSort.NameAscending, ignored),
    };

    private static GalleryThumbnailSize ReadGalleryThumbnailSize(string? value, List<string> ignored) => value switch
    {
        null => GalleryThumbnailSize.Medium,
        "small" => GalleryThumbnailSize.Small,
        "medium" => GalleryThumbnailSize.Medium,
        "large" => GalleryThumbnailSize.Large,
        _ => Fallback("gallery thumbnail size", value, GalleryThumbnailSize.Medium, ignored),
    };

    private static GalleryPlacement ReadGalleryPlacement(string? value, List<string> ignored) => value switch
    {
        null or "right" => GalleryPlacement.Right,
        "left" => GalleryPlacement.Left,
        "top" => GalleryPlacement.Top,
        "bottom" => GalleryPlacement.Bottom,
        _ => Fallback("gallery placement", value, GalleryPlacement.Right, ignored),
    };

    private static LogLevel ReadLogLevel(string? value, List<string> ignored) => value switch
    {
        null or "info" => LogLevel.Info,
        "debug" => LogLevel.Debug,
        "warning" => LogLevel.Warning,
        "error" => LogLevel.Error,
        _ => Fallback("log level", value, LogLevel.Info, ignored),
    };

    private static bool ReadOptionalBoolean(string? value, bool defaultValue, List<string> ignored) => value switch
    {
        null => defaultValue,
        "true" => true,
        "false" => false,
        _ => Fallback("boolean", value, defaultValue, ignored),
    };

    private static float ReadOptionalFloat(
        string? value,
        float defaultValue,
        float minimum,
        List<string> ignored)
    {
        if (value is null)
        {
            return defaultValue;
        }

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            && float.IsFinite(result)
            && result >= minimum)
        {
            return result;
        }

        return Fallback("number", value, defaultValue, ignored);
    }

    // A malformed value costs that one setting, never the rest of the file.
    private static T Fallback<T>(string name, string? value, T defaultValue, List<string> ignored)
    {
        Log.Warning("Settings", $"Ignored unknown {name} '{value}'.");
        ignored.Add($"{name} '{value}'");
        return defaultValue;
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

    private static string WriteGalleryPlacement(GalleryPlacement value) => value switch
    {
        GalleryPlacement.Right => "right",
        GalleryPlacement.Left => "left",
        GalleryPlacement.Top => "top",
        GalleryPlacement.Bottom => "bottom",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string WriteLogLevel(LogLevel value) => value switch
    {
        LogLevel.Debug => "debug",
        LogLevel.Info => "info",
        LogLevel.Warning => "warning",
        LogLevel.Error => "error",
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
