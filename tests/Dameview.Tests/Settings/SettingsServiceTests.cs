using System.Collections.Concurrent;
using System.Diagnostics;
using Dameview.Navigation;
using Dameview.Serialization;
using Dameview.Settings;
using Dameview.Win32;

namespace Dameview.Tests.Settings;

[TestClass]
public sealed class SettingsServiceTests
{
    [TestMethod]
    public void CreatesDefaultsAndPersistsTypedUpdates()
    {
        using var files = new SettingsFiles();
        using SettingsService settings = files.CreateService();
        settings.Start();
        Assert.Contains("theme=dark", File.ReadAllText(files.Path));
        settings.Update(new AppSettings
        {
            Theme = ThemeId.Light,
            AnimationsEnabled = false,
            Sort = FolderSort.SizeLargest,
            GalleryThumbnailSize = GalleryThumbnailSize.Large,
            GalleryWidthDips = 240.0f,
            Window = new WindowPlacementState { X = 20, Y = 30, Width = 320, Height = 240 },
        });
        Assert.IsNull(settings.Error);

        using SettingsService reopened = files.CreateService();
        reopened.Start();
        Assert.AreEqual(settings.Current, reopened.Current);
        StringAssert.Contains(File.ReadAllText(files.Path), "sizeLargest");
    }

    [TestMethod]
    [DataRow(319, 240)]
    [DataRow(320, 239)]
    public void UndersizedWindowDoesNotReplaceOrPersistSettings(int width, int height)
    {
        using var files = new SettingsFiles();
        using SettingsService settings = files.CreateService();
        settings.Start();
        AppSettings previous = settings.Current;
        string saved = File.ReadAllText(files.Path);

        Assert.ThrowsExactly<IniFormatException>(() => settings.Update(previous with
        {
            Window = new WindowPlacementState { Width = width, Height = height },
        }));

        Assert.AreEqual(previous, settings.Current);
        Assert.AreEqual(saved, File.ReadAllText(files.Path));
    }

    [TestMethod]
    public void MissingPropertiesUseDefaults()
    {
        using var files = new SettingsFiles();
        File.WriteAllText(files.Path, "theme=light");
        using SettingsService settings = files.CreateService();
        settings.Start();
        Assert.AreEqual(ThemeId.Light, settings.Current.Theme);
        Assert.IsTrue(settings.Current.AnimationsEnabled);
        Assert.AreEqual(FolderSort.NameAscending, settings.Current.Sort);
        Assert.AreEqual(GalleryThumbnailSize.Medium, settings.Current.GalleryThumbnailSize);
        Assert.AreEqual(AppSettings.DefaultGalleryWidthDips, settings.Current.GalleryWidthDips);
    }

    [TestMethod]
    public void UnknownPropertiesAreIgnoredForForwardCompatibility()
    {
        using var files = new SettingsFiles();
        File.WriteAllText(files.Path, "theme=light\nfutureOption=true\n[window]\nx=0\ny=0\nwidth=1200\nheight=800\nmaximized=false\nunknown=true");
        using SettingsService settings = files.CreateService();
        settings.Start();

        Assert.AreEqual(ThemeId.Light, settings.Current.Theme);
        Assert.AreEqual(FolderSort.NameAscending, settings.Current.Sort);
        Assert.IsNull(settings.Error);
    }

    [TestMethod]
    public void ExternalReplacementIsDeliveredOnTheOwningThreadOnce()
    {
        using var files = new SettingsFiles();
        using SettingsService settings = files.CreateService();
        settings.Start();
        int changes = 0;
        int ownerThread = Environment.CurrentManagedThreadId;
        settings.Changed += (previous, current) =>
        {
            Assert.AreEqual(ownerThread, Environment.CurrentManagedThreadId);
            Assert.AreEqual(ThemeId.Dark, previous.Theme);
            Assert.AreEqual(ThemeId.Light, current.Theme);
            changes++;
        };
        string replacement = files.Path + ".tmp";
        File.WriteAllText(replacement, "theme=light");
        File.Move(replacement, files.Path, overwrite: true);
        files.PumpUntil(() => changes == 1);
        settings.Update(settings.Current);
        // Force a later, distinct reload through a malformed file.
        File.WriteAllText(files.Path, "theme");
        files.PumpUntil(() => settings.Error is not null);
        Assert.AreEqual(1, changes);
        Assert.AreEqual(ThemeId.Light, settings.Current.Theme);
    }

    [TestMethod]
    public void InvalidStartupFileIsPreservedAndLaterEditsRecover()
    {
        using var files = new SettingsFiles();
        const string broken = "theme=purple";
        File.WriteAllText(files.Path, broken);
        using SettingsService settings = files.CreateService();
        settings.Start();
        files.PumpUntil(() => settings.Error is not null);
        Assert.AreEqual(new AppSettings(), settings.Current);
        Assert.AreEqual(broken, File.ReadAllText(files.Path));
        File.WriteAllText(files.Path, "sort=nameDescending");
        files.PumpUntil(() => settings.Current.Sort == FolderSort.NameDescending);
        Assert.IsNull(settings.Error);
    }

    [TestMethod]
    public void NamedThemesPersistAcrossReload()
    {
        foreach (ThemeId theme in Enum.GetValues<ThemeId>())
        {
            using var files = new SettingsFiles();
            using SettingsService settings = files.CreateService();
            settings.Start();
            settings.Update(new AppSettings { Theme = theme });
            Assert.IsNull(settings.Error);

            using SettingsService reopened = files.CreateService();
            reopened.Start();
            Assert.AreEqual(theme, reopened.Current.Theme);
        }
    }

    [TestMethod]
    [DataRow("theme=")]
    [DataRow("theme=42")]
    [DataRow("animations=maybe")]
    [DataRow("sort=random")]
    [DataRow("galleryThumbnailSize=huge")]
    [DataRow("galleryWidth=small")]
    [DataRow("galleryWidth=119")]
    [DataRow("galleryWidth=NaN")]
    public void InvalidValuesDoNotReplaceCurrentSettings(string json)
    {
        using var files = new SettingsFiles();
        using SettingsService settings = files.CreateService();
        settings.Start();
        settings.Update(new AppSettings { Theme = ThemeId.Light });
        File.WriteAllText(files.Path, json);
        files.PumpUntil(() => settings.Error is not null);
        Assert.AreEqual(ThemeId.Light, settings.Current.Theme);
    }

    [TestMethod]
    public void TemporaryReadLockRecoversWithoutAnError()
    {
        using var files = new SettingsFiles();
        using SettingsService settings = files.CreateService();
        settings.Start();
        File.WriteAllText(files.Path, "theme=light");
        using (var locked = new FileStream(files.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            files.PumpUntil(() => !files.Posted.IsEmpty, drain: false);
            files.Drain();
            Assert.IsNull(settings.Error);
            Assert.AreEqual(ThemeId.Dark, settings.Current.Theme);
        }

        files.PumpUntil(() => settings.Current.Theme == ThemeId.Light);
        Assert.IsNull(settings.Error);
    }

    [TestMethod]
    public void SaveFailureKeepsLiveChangeAndReportsFailure()
    {
        using var files = new SettingsFiles();
        using SettingsService settings = files.CreateService();
        settings.Start();
        files.PumpUntil(() => !files.Posted.IsEmpty, drain: false);
        using var locked = new FileStream(files.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        settings.Update(new AppSettings { Theme = ThemeId.Light });
        files.Drain();
        Assert.AreEqual(ThemeId.Light, settings.Current.Theme);
        Assert.IsNotNull(settings.Error);
        StringAssert.Contains(settings.Error, "Could not save");
    }

    [TestMethod]
    public void QueuedReloadAfterDisposalDoesNothing()
    {
        using var files = new SettingsFiles();
        SettingsService settings = files.CreateService();
        settings.Start();
        File.WriteAllText(files.Path, "theme=light");
        files.PumpUntil(() => !files.Posted.IsEmpty, drain: false);
        settings.Dispose();
        files.Drain();
        Assert.AreEqual(ThemeId.Dark, settings.Current.Theme);
    }

    private sealed class SettingsFiles : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "Dameview-settings-tests-" + Guid.NewGuid());
        internal ConcurrentQueue<Action> Posted { get; } = new();
        internal string Path => System.IO.Path.Combine(_directory, "settings.ini");

        internal SettingsFiles()
        {
            Directory.CreateDirectory(_directory);
        }

        internal SettingsService CreateService()
        {
            return new SettingsService(Path, new WindowSynchronizationContext(Posted.Enqueue));
        }

        internal void Drain()
        {
            while (Posted.TryDequeue(out Action? action))
            {
                action();
            }
        }

        internal void PumpUntil(Func<bool> condition, bool drain = true)
        {
            var timeout = Stopwatch.StartNew();
            while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(5))
            {
                if (drain)
                {
                    Drain();
                }

                Thread.Sleep(10);
            }

            Assert.IsTrue(condition(), "Settings watcher did not deliver the expected state.");
        }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
