using Dameview.Navigation;
using Dameview.Platform;
using Dameview.Serialization;

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
            throw new IniFormatException("Unknown theme or sort value.");
        }

        if (Window is not null && !Window.IsUsable)
        {
            throw new IniFormatException("Window dimensions are too small.");
        }
    }
}
