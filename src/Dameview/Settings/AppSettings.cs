using Dameview.Commands;
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
    internal const int MinimumWindowWidth = 320;
    internal const int MinimumWindowHeight = 240;
    internal const float MinimumWheelZoomPercent = 5.0f;
    internal const float MaximumWheelZoomPercent = 50.0f;
    internal const int MinimumPreloadAhead = 1;
    internal const int MaximumPreloadAhead = 10;
    internal const int MinimumImageCacheMegabytes = 128;
    internal const int MaximumImageCacheMegabytes = 4096;

    public ThemeId Theme { get; init; } = ThemeId.Dark;
    public bool AnimationsEnabled { get; init; } = true;
    public bool SharpPixelsWhenZoomed { get; init; }
    /// <summary>How much one wheel notch zooms in, as a percentage of the current zoom.</summary>
    public float WheelZoomPercent { get; init; } = 20.0f;
    /// <summary>How many images to load ahead in the direction of browsing.</summary>
    public int PreloadAhead { get; init; } = 3;
    /// <summary>How much video memory decoded images may keep, preloaded ones included.</summary>
    public int ImageCacheMegabytes { get; init; } = 512;
    public bool SingleInstance { get; init; } = true;
    public bool CheckForUpdatesAutomatically { get; init; } = true;
    /// <summary>When the last check for updates succeeded, so automatic checks stay rare.</summary>
    public DateTimeOffset LastUpdateCheck { get; init; } = DateTimeOffset.MinValue;
    public FolderSort Sort { get; init; } = FolderSort.NameAscending;
    public bool AutoBalancePanes { get; init; }
    public ViewerKeyBindings KeyBindings { get; init; } = ViewerKeyBindings.Defaults;
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

        if (!float.IsFinite(WheelZoomPercent)
            || WheelZoomPercent < MinimumWheelZoomPercent
            || WheelZoomPercent > MaximumWheelZoomPercent)
        {
            throw new IniFormatException("Wheel zoom is invalid.");
        }

        if (PreloadAhead is < MinimumPreloadAhead or > MaximumPreloadAhead)
        {
            throw new IniFormatException("Preload count is invalid.");
        }

        if (ImageCacheMegabytes is < MinimumImageCacheMegabytes or > MaximumImageCacheMegabytes)
        {
            throw new IniFormatException("Image cache size is invalid.");
        }

        if (Window is { } window && (window.Width < MinimumWindowWidth || window.Height < MinimumWindowHeight))
        {
            throw new IniFormatException("Window dimensions are too small.");
        }
    }
}

internal sealed record LoggingSettings
{
    public LogLevel Level { get; init; } = LogLevel.Info;
}
