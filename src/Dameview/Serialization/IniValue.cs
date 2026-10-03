using System.Globalization;

namespace Dameview.Serialization;

// How typed values are spelled in an INI file. Parsers return null for anything they can't read.
internal static class IniValue
{
    internal static string FormatBool(bool value) => value ? "true" : "false";

    internal static bool? ParseBool(string text) => text switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    internal static string FormatInt(int value) => value.ToString(CultureInfo.InvariantCulture);

    internal static int? ParseInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : null;

    internal static string FormatTime(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    internal static DateTimeOffset? ParseTime(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset value)
            ? value
            : null;

    internal static string FormatFloat(float value) => value.ToString(CultureInfo.InvariantCulture);

    internal static float? ParseFloat(string text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
            && float.IsFinite(result)
                ? result
                : null;

    // Enum values are stored as their names in camelCase, so renaming a member changes the file format.
    internal static string FormatEnum<T>(T value)
        where T : struct, Enum
    {
        string name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    internal static T? ParseEnum<T>(string text)
        where T : struct, Enum
    {
        foreach (T candidate in Enum.GetValues<T>())
        {
            if (FormatEnum(candidate) == text)
            {
                return candidate;
            }
        }

        return null;
    }
}
