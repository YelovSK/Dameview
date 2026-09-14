using System.Collections.Concurrent;
using Dameview.Updates;
using Dameview.Win32;

namespace Dameview.Tests.Updates;

[TestClass]
public sealed class UpdateServiceTests
{
    [TestMethod]
    public void PortableCopyDoesNotCheckForUpdates()
    {
        var client = new FakeUpdateClient(new AppRelease("v2.0.0", new Version(2, 0, 0, 0)));
        var service = new UpdateService(client, new WindowSynchronizationContext(_ => { }), currentVersion: null);

        service.Activate();

        Assert.AreEqual(UpdateStatus.Unavailable, service.State.Status);
        Assert.AreEqual(0, client.CheckCount);
    }

    [TestMethod]
    public void NewerReleaseCanBeDownloadedAndApplied()
    {
        var release = new AppRelease("v2.0.0", new Version(2, 0, 0, 0));
        var client = new FakeUpdateClient(release);
        var queue = new ConcurrentQueue<Action>();
        var service = new UpdateService(client, new WindowSynchronizationContext(queue.Enqueue), new Version(1, 0, 0, 0));
        string? downloadedPath = null;
        service.UpdateDownloaded += path => downloadedPath = path;

        service.Activate();
        DispatchNext(queue);

        Assert.AreEqual(UpdateStatus.Available, service.State.Status);
        service.Activate();
        DispatchNext(queue);

        Assert.AreEqual(UpdateStatus.Applying, service.State.Status);
        Assert.AreEqual(client.DownloadPath, downloadedPath);
        Assert.AreEqual(1, client.CheckCount);
        Assert.AreEqual(1, client.DownloadCount);
    }

    [TestMethod]
    public void CurrentReleaseDoesNotDownload()
    {
        var release = new AppRelease("v1.0.0", new Version(1, 0, 0, 0));
        var client = new FakeUpdateClient(release);
        var queue = new ConcurrentQueue<Action>();
        var service = new UpdateService(client, new WindowSynchronizationContext(queue.Enqueue), new Version(1, 0, 0, 0));

        service.Activate();
        DispatchNext(queue);

        Assert.AreEqual(UpdateStatus.Current, service.State.Status);
        Assert.AreEqual(0, client.DownloadCount);
    }

    private static void DispatchNext(ConcurrentQueue<Action> queue)
    {
        Action? action = null;
        bool received = SpinWait.SpinUntil(() => queue.TryDequeue(out action), TimeSpan.FromSeconds(5));
        Assert.IsTrue(received, "The background update operation did not post a result.");
        action!();
    }

    private sealed class FakeUpdateClient(AppRelease release) : IUpdateClient
    {
        internal string DownloadPath { get; } = @"C:\Temp\Dameview.update.exe";
        internal int CheckCount { get; private set; }
        internal int DownloadCount { get; private set; }

        public AppRelease GetLatestRelease()
        {
            CheckCount++;
            return release;
        }

        public string Download(AppRelease requestedRelease)
        {
            Assert.AreSame(release, requestedRelease);
            DownloadCount++;
            return DownloadPath;
        }
    }
}
