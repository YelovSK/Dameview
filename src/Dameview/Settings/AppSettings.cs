using Dameview.Navigation;
using Dameview.Diagnostics;
using Dameview.Serialization;
using Dameview.Win32;

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

internal enum GalleryPlacement
{
    Right,
    Left,
    Top,
    Bottom,
}

internal sealed record AppSettings
{
    internal const float DefaultGallerySizeDips = 184.0f;
    internal const float MinimumGallerySizeDips = 120.0f;

    public ThemeId Theme { get; init; } = ThemeId.Dark;
    public bool AnimationsEnabled { get; init; } = true;
    public bool SingleInstance { get; init; } = true;
    public FolderSort Sort { get; init; } = FolderSort.NameAscending;
    public bool EqualizePanesOnSplit { get; init; }
    public bool GalleryEnabled { get; init; } = true;
    public GalleryPlacement GalleryPlacement { get; init; } = GalleryPlacement.Right;
    public GalleryThumbnailSize GalleryThumbnailSize { get; init; } = GalleryThumbnailSize.Medium;
    public float GallerySizeDips { get; init; } = DefaultGallerySizeDips;
    public WindowPlacementState? Window { get; init; }
    public LoggingSettings Logging { get; init; } = new();

    internal void Validate()
    {
        if (!Enum.IsDefined(Theme)
            || !Enum.IsDefined(Sort)
            || !Enum.IsDefined(GalleryPlacement)
            || !Enum.IsDefined(GalleryThumbnailSize))
        {
            throw new IniFormatException("Unknown settings value.");
        }

        if (!Enum.IsDefined(Logging.Level))
        {
            throw new IniFormatException("Unknown log level.");
        }

        if (!float.IsFinite(GallerySizeDips) || GallerySizeDips < MinimumGallerySizeDips)
        {
            throw new IniFormatException("Gallery size is invalid.");
        }

        if (Window is { } window && (window.Width < 320 || window.Height < 240))
        {
            throw new IniFormatException("Window dimensions are too small.");
        }
    }
}

internal sealed record LoggingSettings
{
    public LogLevel Level { get; init; } = LogLevel.Info;
}
