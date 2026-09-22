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
        AppSettings defaults = new();
        AppSettings settings = new()
        {
            Theme = ReadEnum(document.Get(string.Empty, "theme"), "theme", defaults.Theme, ignored),
            AnimationsEnabled = ReadOptionalBoolean(
                document.Get(string.Empty, "animations"), defaults.AnimationsEnabled, ignored),
            SingleInstance = ReadOptionalBoolean(
                document.Get(string.Empty, "singleInstance"), defaults.SingleInstance, ignored),
            AutoBalancePanes = ReadOptionalBoolean(
                document.Get(string.Empty, "autoBalancePanes"), defaults.AutoBalancePanes, ignored),
            Sort = ReadEnum(document.Get(string.Empty, "sort"), "sort", defaults.Sort, ignored),
            GalleryEnabled = ReadOptionalBoolean(
                document.Get(string.Empty, "galleryEnabled"), defaults.GalleryEnabled, ignored),
            GalleryPlacement = ReadEnum(
                document.Get(string.Empty, "galleryPlacement"), "gallery placement", defaults.GalleryPlacement, ignored),
            GalleryThumbnailSize = ReadEnum(
                document.Get(string.Empty, "galleryThumbnailSize"),
                "gallery thumbnail size",
                defaults.GalleryThumbnailSize,
                ignored),
            GallerySizeDips = ReadOptionalFloat(
                document.Get(string.Empty, "gallerySize"),
                defaults.GallerySizeDips,
                AppSettings.MinimumGallerySizeDips,
                ignored),
            Window = ReadWindow(document, ignored),
            Logging = new LoggingSettings
            {
                Level = ReadEnum(document.Get("logging", "level"), "log level", defaults.Logging.Level, ignored),
            },
            KeyBindings = ReadKeyBindings(document, ignored),
        };
        return (settings, ignored);
    }

    internal static string Write(AppSettings settings)
    {
        var document = new IniDocument();
        document.Set(string.Empty, "theme", WriteEnum(settings.Theme));
        document.Set(string.Empty, "animations", settings.AnimationsEnabled ? "true" : "false");
        document.Set(string.Empty, "singleInstance", settings.SingleInstance ? "true" : "false");
        document.Set(
            string.Empty,
            "autoBalancePanes",
            settings.AutoBalancePanes ? "true" : "false");
        document.Set(string.Empty, "sort", WriteEnum(settings.Sort));
        document.Set(string.Empty, "galleryEnabled", settings.GalleryEnabled ? "true" : "false");
        document.Set(string.Empty, "galleryPlacement", WriteEnum(settings.GalleryPlacement));
        document.Set(string.Empty, "galleryThumbnailSize", WriteEnum(settings.GalleryThumbnailSize));
        document.Set(string.Empty, "gallerySize", settings.GallerySizeDips.ToString(CultureInfo.InvariantCulture));
        if (settings.Window is { } window)
        {
            document.Set("window", "x", window.X.ToString(CultureInfo.InvariantCulture));
            document.Set("window", "y", window.Y.ToString(CultureInfo.InvariantCulture));
            document.Set("window", "width", window.Width.ToString(CultureInfo.InvariantCulture));
            document.Set("window", "height", window.Height.ToString(CultureInfo.InvariantCulture));
            document.Set("window", "maximized", window.Maximized ? "true" : "false");
        }

        document.Set("logging", "level", WriteEnum(settings.Logging.Level));
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
            if (document.Get("keybindings", WriteEnum(command.Id)) is string value)
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
                WriteEnum(command.Id),
                string.Join(' ', bindings.GetShortcuts(command.Id).Select(shortcut => shortcut.Text)));
        }
    }

    // Enum values are stored as their names in camelCase, so renaming a member changes the file format.
    private static string WriteEnum<T>(T value)
        where T : struct, Enum
    {
        string name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static T ReadEnum<T>(string? value, string name, T defaultValue, List<string> ignored)
        where T : struct, Enum
    {
        if (value is null)
        {
            return defaultValue;
        }

        foreach (T candidate in Enum.GetValues<T>())
        {
            if (WriteEnum(candidate) == value)
            {
                return candidate;
            }
        }

        return Fallback(name, value, defaultValue, ignored);
    }

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
}
