using Dameview.Navigation;
using Dameview.Platform;
using Dameview.Serialization;

namespace Dameview.Settings;

internal enum ThemeId
{
    Dark,
    Light,
    CatppuccinFrappe,
    CatppuccinMacchiato,
    CatppuccinMocha,
    GruvboxDark,
    Nord,
    Dracula,
    RosePine,
}

internal enum GalleryThumbnailSize
{
    Small,
    Medium,
    Large,
}

internal sealed record AppSettings
{
    internal const float DefaultGalleryWidthDips = 184.0f;
    internal const float MinimumGalleryWidthDips = 120.0f;

    public ThemeId Theme { get; init; } = ThemeId.Dark;
    public bool AnimationsEnabled { get; init; } = true;
    public FolderSort Sort { get; init; } = FolderSort.NameAscending;
    public GalleryThumbnailSize GalleryThumbnailSize { get; init; } = GalleryThumbnailSize.Medium;
    public float GalleryWidthDips { get; init; } = DefaultGalleryWidthDips;
    public WindowPlacementState? Window { get; init; }

    internal void Validate()
    {
        if (!Enum.IsDefined(Theme)
            || !Enum.IsDefined(Sort)
            || !Enum.IsDefined(GalleryThumbnailSize))
        {
            throw new IniFormatException("Unknown settings value.");
        }

        if (!float.IsFinite(GalleryWidthDips) || GalleryWidthDips < MinimumGalleryWidthDips)
        {
            throw new IniFormatException("Gallery width is invalid.");
        }

        if (Window is { } window && (window.Width < 320 || window.Height < 240))
        {
            throw new IniFormatException("Window dimensions are too small.");
        }
    }
}
