using Dameview.Navigation;
using Dameview.Platform;
using Dameview.Serialization;
using Dameview.UI;

namespace Dameview.Settings;

internal enum GalleryThumbnailSize
{
    Small,
    Medium,
    Large,
}

internal sealed record AppSettings
{
    public Theme Theme { get; init; } = Themes.Dark;
    public bool AnimationsEnabled { get; init; } = true;
    public FolderSort Sort { get; init; } = FolderSort.NameAscending;
    public GalleryThumbnailSize GalleryThumbnailSize { get; init; } = GalleryThumbnailSize.Medium;
    public float GalleryWidthDips { get; init; } = UiDesign.DefaultGalleryWidth;
    public WindowPlacementState? Window { get; init; }

    internal void Validate()
    {
        if (!Themes.All.Contains(Theme)
            || !Enum.IsDefined(Sort)
            || !Enum.IsDefined(GalleryThumbnailSize))
        {
            throw new IniFormatException("Unknown settings value.");
        }

        if (!float.IsFinite(GalleryWidthDips) || GalleryWidthDips < UiDesign.MinimumPaneSize)
        {
            throw new IniFormatException("Gallery width is invalid.");
        }

        if (Window is not null && !Window.IsUsable)
        {
            throw new IniFormatException("Window dimensions are too small.");
        }
    }
}
