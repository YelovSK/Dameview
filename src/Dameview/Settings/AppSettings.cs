using Dameview.Navigation;
using Dameview.Platform;
using Dameview.Serialization;
using Dameview.UI;

namespace Dameview.Settings;

internal sealed record AppSettings
{
    public Theme Theme { get; init; } = Themes.Dark;
    public FolderSort Sort { get; init; } = FolderSort.NameAscending;
    public WindowPlacementState? Window { get; init; }

    internal void Validate()
    {
        if (!Themes.All.Contains(Theme) || !Enum.IsDefined(Sort))
        {
            throw new IniFormatException("Unknown theme or sort value.");
        }

        if (Window is not null && !Window.IsUsable)
        {
            throw new IniFormatException("Window dimensions are too small.");
        }
    }
}
