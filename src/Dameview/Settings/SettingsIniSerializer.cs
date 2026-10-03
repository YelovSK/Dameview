using Dameview.Commands;
using Dameview.Diagnostics;
using Dameview.Serialization;
using Dameview.Win32;
using Binding = Dameview.Serialization.IniBinding<Dameview.Settings.AppSettings>;

namespace Dameview.Settings;

internal static class SettingsIniSerializer
{
    private const string Root = "";

    private static readonly Binding[] Bindings =
    [
        Binding.Enum(Root, "theme", s => s.Theme, (s, value) => s with { Theme = value }),
        Binding.Bool(Root, "animations", s => s.AnimationsEnabled, (s, value) => s with { AnimationsEnabled = value }),
        Binding.Bool(Root, "singleInstance", s => s.SingleInstance, (s, value) => s with { SingleInstance = value }),
        Binding.Bool(Root, "autoBalancePanes", s => s.AutoBalancePanes, (s, value) => s with { AutoBalancePanes = value }),
        Binding.Bool(
            Root,
            "sharpPixelsWhenZoomed",
            s => s.SharpPixelsWhenZoomed,
            (s, value) => s with { SharpPixelsWhenZoomed = value }),
        Binding.Enum(Root, "sort", s => s.Sort, (s, value) => s with { Sort = value }),
        Binding.Bool(Root, "galleryEnabled", s => s.GalleryEnabled, (s, value) => s with { GalleryEnabled = value }),
        Binding.Enum(
            Root,
            "galleryPlacement",
            s => s.GalleryPlacement,
            (s, value) => s with { GalleryPlacement = value }),
        Binding.Enum(
            Root,
            "galleryThumbnailSize",
            s => s.GalleryThumbnailSize,
            (s, value) => s with { GalleryThumbnailSize = value }),
        Binding.Float(
            Root,
            "gallerySize",
            AppSettings.MinimumGallerySizeDips,
            s => s.GallerySizeDips,
            (s, value) => s with { GallerySizeDips = value }),
        Binding.Float(
            Root,
            "wheelZoomPercent",
            AppSettings.MinimumWheelZoomPercent,
            s => s.WheelZoomPercent,
            (s, value) => s with { WheelZoomPercent = value },
            AppSettings.MaximumWheelZoomPercent),
        Binding.Enum(
            "logging",
            "level",
            s => s.Logging.Level,
            (s, value) => s with { Logging = s.Logging with { Level = value } }),
        Binding.Custom(
            (document, s, ignored) => s with { Window = ReadWindow(document, ignored) },
            (document, s) => WriteWindow(document, s.Window)),
        Binding.Custom(
            (document, s, ignored) => s with { KeyBindings = ReadKeyBindings(document, s.KeyBindings, ignored) },
            (document, s) => WriteKeyBindings(document, s.KeyBindings)),
    ];

    /// <returns>The settings, and every value that was unreadable and fell back to its default.</returns>
    internal static (AppSettings Settings, IReadOnlyList<string> Ignored) Read(string text)
    {
        var document = IniDocument.Parse(text);
        List<string> ignored = [];
        AppSettings settings = new();
        foreach (Binding binding in Bindings)
        {
            settings = binding.Read(document, settings, ignored);
        }

        // A malformed value costs that one setting, never the rest of the file.
        foreach (string value in ignored)
        {
            Log.Warning("Settings", $"Ignored {value}.");
        }

        return (settings, ignored);
    }

    /// <param name="existing">The file being replaced, whose comments and unknown keys are kept.</param>
    internal static string Write(AppSettings settings, string existing = "")
    {
        var document = IniDocument.Parse(existing);
        foreach (Binding binding in Bindings)
        {
            binding.Write(document, settings);
        }

        return document.Write();
    }

    // The placement only means anything complete, so any unreadable part discards it.
    private static WindowPlacementState? ReadWindow(IniDocument document, List<string> ignored)
    {
        if (!document.HasSection("window"))
        {
            return null;
        }

        if (IniValue.ParseInt(document.Get("window", "x")) is not int x
            || IniValue.ParseInt(document.Get("window", "y")) is not int y
            || IniValue.ParseInt(document.Get("window", "width")) is not int width
            || IniValue.ParseInt(document.Get("window", "height")) is not int height)
        {
            ignored.Add("an incomplete window placement");
            return null;
        }

        if (width < AppSettings.MinimumWindowWidth || height < AppSettings.MinimumWindowHeight)
        {
            ignored.Add($"window size '{width}x{height}'");
            return null;
        }

        bool maximized = false;
        if (document.Get("window", "maximized") is string text)
        {
            if (IniValue.ParseBool(text) is bool value)
            {
                maximized = value;
            }
            else
            {
                ignored.Add($"window.maximized '{text}'");
            }
        }

        return new WindowPlacementState { X = x, Y = y, Width = width, Height = height, Maximized = maximized };
    }

    private static void WriteWindow(IniDocument document, WindowPlacementState? window)
    {
        if (window is null)
        {
            document.RemoveSection("window");
            return;
        }

        document.Set("window", "x", IniValue.FormatInt(window.X));
        document.Set("window", "y", IniValue.FormatInt(window.Y));
        document.Set("window", "width", IniValue.FormatInt(window.Width));
        document.Set("window", "height", IniValue.FormatInt(window.Height));
        document.Set("window", "maximized", IniValue.FormatBool(window.Maximized));
    }

    private static ViewerKeyBindings ReadKeyBindings(
        IniDocument document,
        ViewerKeyBindings bindings,
        List<string> ignored)
    {
        foreach (Command command in AppCommands.All)
        {
            if (document.Get("keybindings", command.Id) is string value)
            {
                bindings = bindings.WithShortcuts(command, ReadShortcuts(value, ignored));
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
                ignored.Add($"shortcut '{part}'");
            }
        }

        return [.. shortcuts];
    }

    private static void WriteKeyBindings(IniDocument document, ViewerKeyBindings bindings)
    {
        foreach (Command command in AppCommands.All)
        {
            document.Set(
                "keybindings",
                command.Id,
                string.Join(' ', bindings.GetShortcuts(command).Select(shortcut => shortcut.Text)));
        }
    }
}
