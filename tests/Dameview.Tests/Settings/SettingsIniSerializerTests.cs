using Dameview.Diagnostics;
using Dameview.Navigation;
using Dameview.Settings;
using Dameview.Win32;

namespace Dameview.Tests.Settings;

[TestClass]
public sealed class SettingsIniSerializerTests
{
    // Existing files only keep reading while the enum members keep their names.
    [TestMethod]
    public void EnumSettingsAreStoredAsTheirNamesInCamelCase()
    {
        var settings = new AppSettings
        {
            Theme = ThemeId.CatppuccinMocha,
            Sort = FolderSort.DateModifiedNewest,
            GalleryPlacement = GalleryPlacement.Bottom,
            GalleryThumbnailSize = GalleryThumbnailSize.Large,
            Logging = new LoggingSettings { Level = LogLevel.Warning },
        };

        string text = SettingsIniSerializer.Write(settings);

        Assert.Contains("theme=catppuccinMocha", text);
        Assert.Contains("sort=dateModifiedNewest", text);
        Assert.Contains("galleryPlacement=bottom", text);
        Assert.Contains("galleryThumbnailSize=large", text);
        Assert.Contains("level=warning", text);
        AssertRoundTrips(settings);
    }

    [TestMethod]
    public void ImageAndUpdateSettingsSurviveAWriteAndRead()
    {
        AssertRoundTrips(new AppSettings
        {
            SharpPixelsWhenZoomed = true,
            WheelZoomPercent = 35.0f,
            CheckForUpdatesAutomatically = false,
            LastUpdateCheck = new DateTimeOffset(2026, 10, 4, 12, 30, 15, TimeSpan.FromHours(2)),
        });
    }

    [TestMethod]
    public void EveryEnumValueSurvivesAWriteAndRead()
    {
        foreach (ThemeId theme in Enum.GetValues<ThemeId>())
        {
            AssertRoundTrips(new AppSettings { Theme = theme });
        }

        foreach (FolderSort sort in Enum.GetValues<FolderSort>())
        {
            AssertRoundTrips(new AppSettings { Sort = sort });
        }

        foreach (LogLevel level in Enum.GetValues<LogLevel>())
        {
            AssertRoundTrips(new AppSettings { Logging = new LoggingSettings { Level = level } });
        }
    }

    [TestMethod]
    public void RewritingItsOwnOutputChangesNothing()
    {
        var settings = new AppSettings
        {
            Theme = ThemeId.Nord,
            Window = new WindowPlacementState { X = 10, Y = 20, Width = 800, Height = 600 },
        };
        string written = SettingsIniSerializer.Write(settings);

        Assert.AreEqual(written, SettingsIniSerializer.Write(settings, written));
    }

    [TestMethod]
    public void CommentsFormattingAndUnknownKeysSurviveASave()
    {
        const string existing =
            "; my settings\n"
            + "theme = light\n"
            + "futureOption=true\n"
            + "\n"
            + "[logging]\n"
            + "# chatty\n"
            + "level=debug\n"
            + "\n"
            + "[future]\n"
            + "key=value\n";
        (AppSettings read, _) = SettingsIniSerializer.Read(existing);

        string written = SettingsIniSerializer.Write(read with { Theme = ThemeId.Dark }, existing);

        string[] lines = written.ReplaceLineEndings("\n").Split('\n');
        Assert.AreEqual("; my settings", lines[0]);
        Assert.AreEqual("theme=dark", lines[1]);
        Assert.AreEqual("futureOption=true", lines[2]);
        Assert.Contains("# chatty\nlevel=debug\n", written.ReplaceLineEndings("\n"));
        Assert.Contains("[future]\nkey=value\n", written.ReplaceLineEndings("\n"));
        Assert.AreEqual(read with { Theme = ThemeId.Dark }, SettingsIniSerializer.Read(written).Settings);
    }

    [TestMethod]
    public void MissingTopLevelKeysAreAddedAboveTheFirstSection()
    {
        const string existing = "[logging]\nlevel=warning\n";

        string written = SettingsIniSerializer.Write(new AppSettings(), existing);

        string normalized = written.ReplaceLineEndings("\n");
        Assert.IsTrue(normalized.StartsWith("theme=dark\n", StringComparison.Ordinal), normalized);
        Assert.Contains("\n\n[logging]\nlevel=info\n", normalized);
        Assert.AreEqual(new AppSettings(), SettingsIniSerializer.Read(written).Settings);
    }

    [TestMethod]
    public void ClearedWindowPlacementIsRemovedFromTheFile()
    {
        string existing = SettingsIniSerializer.Write(new AppSettings
        {
            Window = new WindowPlacementState { X = 10, Y = 20, Width = 800, Height = 600 },
        });

        string written = SettingsIniSerializer.Write(new AppSettings(), existing);

        Assert.DoesNotContain("[window]", written);
        Assert.AreEqual(SettingsIniSerializer.Write(new AppSettings()), written);
    }

    private static void AssertRoundTrips(AppSettings settings)
    {
        (AppSettings read, IReadOnlyList<string> ignored) =
            SettingsIniSerializer.Read(SettingsIniSerializer.Write(settings));

        Assert.IsEmpty(ignored, "A value this app wrote must be one it can read back.");
        Assert.AreEqual(settings, read);
    }
}
