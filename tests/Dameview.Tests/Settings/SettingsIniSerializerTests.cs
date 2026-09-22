using Dameview.Diagnostics;
using Dameview.Navigation;
using Dameview.Settings;

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

    private static void AssertRoundTrips(AppSettings settings)
    {
        (AppSettings read, IReadOnlyList<string> ignored) =
            SettingsIniSerializer.Read(SettingsIniSerializer.Write(settings));

        Assert.IsEmpty(ignored, "A value this app wrote must be one it can read back.");
        Assert.AreEqual(settings, read);
    }
}
