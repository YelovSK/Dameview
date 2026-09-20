using Dameview.Diagnostics;
using Dameview.Navigation;
using Dameview.Settings;

namespace Dameview.Tests.Settings;

[TestClass]
public sealed class SettingsIniSerializerTests
{
    // Writing uses a hand-written name per enum value and throws on one it does not know,
    // so adding a value and forgetting its name would stop settings saving at all.
    [TestMethod]
    public void EveryThemeSurvivesAWriteAndRead()
    {
        foreach (ThemeId theme in Enum.GetValues<ThemeId>())
        {
            AssertRoundTrips(new AppSettings { Theme = theme });
        }
    }

    [TestMethod]
    public void EverySortOrderSurvivesAWriteAndRead()
    {
        foreach (FolderSort sort in Enum.GetValues<FolderSort>())
        {
            AssertRoundTrips(new AppSettings { Sort = sort });
        }
    }

    [TestMethod]
    public void EveryGalleryPlacementSurvivesAWriteAndRead()
    {
        foreach (GalleryPlacement placement in Enum.GetValues<GalleryPlacement>())
        {
            AssertRoundTrips(new AppSettings { GalleryPlacement = placement });
        }
    }

    [TestMethod]
    public void EveryThumbnailSizeSurvivesAWriteAndRead()
    {
        foreach (GalleryThumbnailSize size in Enum.GetValues<GalleryThumbnailSize>())
        {
            AssertRoundTrips(new AppSettings { GalleryThumbnailSize = size });
        }
    }

    [TestMethod]
    public void EveryLogLevelSurvivesAWriteAndRead()
    {
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
