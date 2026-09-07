using System.Globalization;
using Dameview.Navigation;
using Dameview.Platform;
using Dameview.Serialization;
using Dameview.UI;

namespace Dameview.Settings;

internal static class SettingsIniSerializer
{
    internal static AppSettings Read(string text)
    {
        var document = IniDocument.Parse(text);
        Theme theme = ReadTheme(document.Get(string.Empty, "theme"));
        FolderSort sort = ReadSort(document.Get(string.Empty, "sort"));
        WindowPlacementState? window = !document.HasSection("window") ? null : new WindowPlacementState
        {
            X = ReadRequiredInt(document.Get("window", "x")),
            Y = ReadRequiredInt(document.Get("window", "y")),
            Width = ReadRequiredInt(document.Get("window", "width")),
            Height = ReadRequiredInt(document.Get("window", "height")),
            Maximized = ReadRequiredBoolean(document.Get("window", "maximized")),
        };

        return new AppSettings { Theme = theme, Sort = sort, Window = window };
    }

    internal static string Write(AppSettings settings)
    {
        var document = new IniDocument();
        document.Set(string.Empty, "theme", WriteTheme(settings.Theme));
        document.Set(string.Empty, "sort", WriteSort(settings.Sort));
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

    private static Theme ReadTheme(string? value)
    {
        if (value is null)
        {
            return Themes.Dark;
        }

        return Themes.FromId(value) ?? throw new IniFormatException("Unknown theme value.");
    }

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

    private static string WriteTheme(Theme value) => value.Id;

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
