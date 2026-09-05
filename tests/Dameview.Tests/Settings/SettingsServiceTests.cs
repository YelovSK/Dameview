using System.Collections.Concurrent;
using System.Diagnostics;
using Dameview.Navigation;
using Dameview.Settings;

namespace Dameview.Tests.Settings;

[TestClass]
public sealed class SettingsServiceTests
{
    [TestMethod]
    public void CreatesDefaultsAndPersistsTypedUpdates()
    {
        using var files = new SettingsFiles();
        using var settings = files.CreateService();
        settings.Start();
        StringAssert.Contains(File.ReadAllText(files.Path), "\"theme\": \"dark\"");
        settings.Update(new AppSettings { Theme = ThemeMode.Light, Sort = FolderSort.SizeLargest });
        Assert.IsNull(settings.Error);

        using var reopened = files.CreateService();
        reopened.Start();
        Assert.AreEqual(settings.Current, reopened.Current);
        StringAssert.Contains(File.ReadAllText(files.Path), "sizeLargest");
    }

    [TestMethod]
    public void MissingPropertiesUseDefaults()
    {
        using var files = new SettingsFiles();
        File.WriteAllText(files.Path, "{\"theme\":\"light\"}");
        using var settings = files.CreateService();
        settings.Start();
        Assert.AreEqual(ThemeMode.Light, settings.Current.Theme);
        Assert.AreEqual(FolderSort.NameAscending, settings.Current.Sort);
    }

    [TestMethod]
    public void ExternalReplacementIsDeliveredOnTheOwningThreadOnce()
    {
        using var files = new SettingsFiles();
        using var settings = files.CreateService();
        settings.Start();
        int changes = 0;
        int ownerThread = Environment.CurrentManagedThreadId;
        settings.Changed += (previous, current) =>
        {
            Assert.AreEqual(ownerThread, Environment.CurrentManagedThreadId);
            Assert.AreEqual(ThemeMode.Dark, previous.Theme);
            Assert.AreEqual(ThemeMode.Light, current.Theme);
            changes++;
        };
        string replacement = files.Path + ".tmp";
        File.WriteAllText(replacement, "{\"theme\":\"light\"}");
        File.Move(replacement, files.Path, overwrite: true);
        files.PumpUntil(() => changes == 1);
        settings.Update(settings.Current);
        // Force a later, distinct reload through a malformed file.
        File.WriteAllText(files.Path, "{");
        files.PumpUntil(() => settings.Error is not null);
        Assert.AreEqual(1, changes);
        Assert.AreEqual(ThemeMode.Light, settings.Current.Theme);
    }

    [TestMethod]
    public void InvalidStartupFileIsPreservedAndLaterEditsRecover()
    {
        using var files = new SettingsFiles();
        const string broken = "{\"theme\":\"purple\"}";
        File.WriteAllText(files.Path, broken);
        using var settings = files.CreateService();
        settings.Start();
        files.PumpUntil(() => settings.Error is not null);
        Assert.AreEqual(new AppSettings(), settings.Current);
        Assert.AreEqual(broken, File.ReadAllText(files.Path));
        File.WriteAllText(files.Path, "{\"sort\":\"nameDescending\"}");
        files.PumpUntil(() => settings.Current.Sort == FolderSort.NameDescending);
        Assert.IsNull(settings.Error);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{\"theme\":42}")]
    [DataRow("{\"sort\":\"random\"}")]
    [DataRow("{\"thmee\":\"light\"}")]
    public void InvalidValuesDoNotReplaceCurrentSettings(string json)
    {
        using var files = new SettingsFiles();
        using var settings = files.CreateService();
        settings.Start();
        settings.Update(new AppSettings { Theme = ThemeMode.Light });
        File.WriteAllText(files.Path, json);
        files.PumpUntil(() => settings.Error is not null);
        Assert.AreEqual(ThemeMode.Light, settings.Current.Theme);
    }

    [TestMethod]
    public void TemporaryReadLockRecoversWithoutAnError()
    {
        using var files = new SettingsFiles();
        using var settings = files.CreateService();
        settings.Start();
        File.WriteAllText(files.Path, "{\"theme\":\"light\"}");
        using (var locked = new FileStream(files.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            files.PumpUntil(() => !files.Posted.IsEmpty, drain: false);
            files.Drain();
            Assert.IsNull(settings.Error);
            Assert.AreEqual(ThemeMode.Dark, settings.Current.Theme);
        }

        files.PumpUntil(() => settings.Current.Theme == ThemeMode.Light);
        Assert.IsNull(settings.Error);
    }

    [TestMethod]
    public void SaveFailureKeepsLiveChangeAndReportsFailure()
    {
        using var files = new SettingsFiles();
        using var settings = files.CreateService();
        settings.Start();
        files.PumpUntil(() => !files.Posted.IsEmpty, drain: false);
        using var locked = new FileStream(files.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        settings.Update(new AppSettings { Theme = ThemeMode.Light });
        files.Drain();
        Assert.AreEqual(ThemeMode.Light, settings.Current.Theme);
        Assert.IsNotNull(settings.Error);
        StringAssert.Contains(settings.Error, "Could not save");
    }

    [TestMethod]
    public void QueuedReloadAfterDisposalDoesNothing()
    {
        using var files = new SettingsFiles();
        var settings = files.CreateService();
        settings.Start();
        File.WriteAllText(files.Path, "{\"theme\":\"light\"}");
        files.PumpUntil(() => !files.Posted.IsEmpty, drain: false);
        settings.Dispose();
        files.Drain();
        Assert.AreEqual(ThemeMode.Dark, settings.Current.Theme);
    }

    private sealed class SettingsFiles : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "Dameview-settings-tests-" + Guid.NewGuid());
        internal ConcurrentQueue<Action> Posted { get; } = new();
        internal string Path => System.IO.Path.Combine(_directory, "settings.json");

        internal SettingsFiles()
        {
            Directory.CreateDirectory(_directory);
        }

        internal SettingsService CreateService()
        {
            return new SettingsService(Path, Posted.Enqueue);
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
