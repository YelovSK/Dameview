using System.Text.Json;
using System.Text.Json.Serialization;
using Dameview.Navigation;
using Dameview.Platform;

namespace Dameview.Settings;

internal enum ThemeMode
{
    Dark,
    Light,
}

internal sealed record AppSettings
{
    public ThemeMode Theme { get; init; } = ThemeMode.Dark;
    public FolderSort Sort { get; init; } = FolderSort.NameAscending;
    public WindowPlacementState? Window { get; init; }

    internal void Validate()
    {
        if (!Enum.IsDefined(Theme) || !Enum.IsDefined(Sort))
        {
            throw new JsonException("Unknown theme or sort value.");
        }

        if (Window is not null && !Window.IsUsable)
        {
            throw new JsonException("Window dimensions are too small.");
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    Converters = new[] { typeof(ThemeModeJsonConverter), typeof(FolderSortJsonConverter) })]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}

internal sealed class ThemeModeJsonConverter : JsonStringEnumConverter<ThemeMode>
{
    public ThemeModeJsonConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    {
    }
}

internal sealed class FolderSortJsonConverter : JsonStringEnumConverter<FolderSort>
{
    public FolderSortJsonConverter() : base(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
    {
    }
}
