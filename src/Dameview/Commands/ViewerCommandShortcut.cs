using Dameview.Win32.Input;

namespace Dameview.Commands;

internal readonly record struct ViewerCommandShortcut(
    WindowKey Key,
    bool Control = false,
    bool Shift = false)
{
    // Keys whose enum name would read badly in the UI and in settings. Anything absent
    // uses the enum name, which parses back through Enum.TryParse.
    private static readonly (WindowKey Key, string Label)[] KeyLabels =
    [
        (WindowKey.Number0, "0"),
        (WindowKey.Number1, "1"),
        (WindowKey.Number2, "2"),
        (WindowKey.Number3, "3"),
        (WindowKey.Number4, "4"),
        (WindowKey.Number5, "5"),
        (WindowKey.Number6, "6"),
        (WindowKey.Number7, "7"),
        (WindowKey.Number8, "8"),
        (WindowKey.Number9, "9"),
        (WindowKey.Semicolon, ";"),
        (WindowKey.Equals, "="),
        (WindowKey.Comma, ","),
        (WindowKey.Minus, "-"),
        (WindowKey.Period, "."),
        (WindowKey.Slash, "/"),
        (WindowKey.Grave, "`"),
        (WindowKey.BracketLeft, "["),
        (WindowKey.Backslash, "\\"),
        (WindowKey.BracketRight, "]"),
        (WindowKey.Quote, "'"),
    ];

    internal string Text
    {
        get
        {
            string control = Control ? "Ctrl+" : string.Empty;
            string shift = Shift ? "Shift+" : string.Empty;
            return control + shift + GetKeyLabel(Key);
        }
    }

    internal bool Matches(WindowKeyEvent input) =>
        input.Key == Key
        && input.Control == Control
        && input.Shift == Shift;

    internal static bool TryParse(string text, out ViewerCommandShortcut shortcut)
    {
        shortcut = default;
        string[] parts = text.Split('+', StringSplitOptions.TrimEntries);
        bool control = false;
        bool shift = false;
        for (int index = 0; index < parts.Length - 1; index++)
        {
            if (string.Equals(parts[index], "Ctrl", StringComparison.OrdinalIgnoreCase))
            {
                control = true;
            }
            else if (string.Equals(parts[index], "Shift", StringComparison.OrdinalIgnoreCase))
            {
                shift = true;
            }
            else
            {
                return false;
            }
        }

        if (!TryParseKey(parts[^1], out WindowKey key))
        {
            return false;
        }

        shortcut = new ViewerCommandShortcut(key, control, shift);
        return true;
    }

    private static string GetKeyLabel(WindowKey key)
    {
        foreach ((WindowKey candidate, string label) in KeyLabels)
        {
            if (candidate == key)
            {
                return label;
            }
        }

        return key.ToString();
    }

    private static bool TryParseKey(string text, out WindowKey key)
    {
        foreach ((WindowKey candidate, string label) in KeyLabels)
        {
            if (string.Equals(label, text, StringComparison.OrdinalIgnoreCase))
            {
                key = candidate;
                return true;
            }
        }

        return Enum.TryParse(text, ignoreCase: true, out key) && Enum.IsDefined(key);
    }
}
