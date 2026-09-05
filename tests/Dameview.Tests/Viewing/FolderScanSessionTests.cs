using System.Collections.Concurrent;
using Dameview.Imaging;
using Dameview.Navigation;
using Dameview.Viewing;

namespace Dameview.Tests.Viewing;

[TestClass]
public sealed class FolderScanSessionTests
{
    [TestMethod]
    public void ImageCanFinishWhileNavigationWaitsForTheFolder()
    {
        using var fixture = new Fixture();
        fixture.Session.OpenImage(Fixture.First);
        Assert.AreEqual(Fixture.First, fixture.Loader.Path);
        fixture.Loader.Complete();
        Assert.AreEqual(Fixture.First, fixture.Session.State.DisplayedImage!.Path);
        Assert.IsTrue(fixture.Session.State.IsScanningFolder);
        fixture.Session.ShowNextImage();
        Assert.AreEqual(1, fixture.Loader.LoadCount);

        fixture.Scanner.Complete(0, Fixture.First, Fixture.Second);
        fixture.DeliverScan();
        Assert.IsFalse(fixture.Session.State.IsScanningFolder);
        Assert.AreEqual(Fixture.Second, fixture.Loader.Preloads[0]);
        fixture.Session.ShowNextImage();
        Assert.AreEqual(Fixture.Second, fixture.Loader.Path);
    }

    [TestMethod]
    public void FolderCanFinishBeforeImageWithoutPreloadingUntilImageCompletes()
    {
        using var fixture = new Fixture();
        fixture.Session.OpenImage(Fixture.First);
        fixture.Scanner.Complete(0, Fixture.First, Fixture.Second);
        fixture.DeliverScan();
        Assert.IsTrue(fixture.Session.State.IsLoading);
        Assert.HasCount(0, fixture.Loader.Preloads);
        fixture.Loader.Complete();
        Assert.AreEqual(Fixture.Second, fixture.Loader.Preloads[0]);
    }

    [TestMethod]
    public void QueuedScanFromPreviousFolderCannotReplaceNewNavigation()
    {
        using var fixture = new Fixture();
        fixture.Session.OpenImage(Fixture.First);
        fixture.Scanner.Complete(0, Fixture.First, Fixture.Second);
        Action oldDelivery = fixture.TakeScan();
        string other = @"C:\other-folder\a.jpg";
        string otherNext = @"C:\other-folder\b.jpg";
        fixture.Session.OpenImage(other);
        Assert.IsTrue(fixture.Scanner.Requests[0].Token.IsCancellationRequested);
        oldDelivery();
        fixture.Session.ShowNextImage();
        Assert.AreEqual(other, fixture.Loader.Path);
        Assert.IsTrue(fixture.Session.State.IsScanningFolder);

        fixture.Scanner.Complete(1, other, otherNext);
        fixture.DeliverScan();
        fixture.Session.ShowNextImage();
        Assert.AreEqual(otherNext, fixture.Loader.Path);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ScanFailureKeepsImageUsableInEitherCompletionOrder(bool imageFirst)
    {
        using var fixture = new Fixture();
        fixture.Session.OpenImage(Fixture.First);
        if (imageFirst)
        {
            fixture.Loader.Complete();
        }

        fixture.Scanner.Requests[0].Completion.SetException(new IOException("Folder unavailable"));
        fixture.DeliverScan();
        if (!imageFirst)
        {
            fixture.Loader.Complete();
        }

        Assert.AreEqual(Fixture.First, fixture.Session.State.DisplayedImage!.Path);
        Assert.IsTrue(fixture.Session.State.IsError);
        StringAssert.Contains(fixture.Session.State.Message!, "Folder unavailable");
        fixture.Session.ShowNextImage();
        Assert.AreEqual(1, fixture.Loader.LoadCount);
    }

    [TestMethod]
    public void ScanFailureDoesNotOverwriteAnImageFailure()
    {
        using var fixture = new Fixture();
        fixture.Session.OpenImage(Fixture.First);
        fixture.Loader.Fail();
        string? error = fixture.Session.State.Message;
        fixture.Scanner.Requests[0].Completion.SetException(new IOException("Folder unavailable"));
        fixture.DeliverScan();
        Assert.AreEqual(error, fixture.Session.State.Message);
    }

    [TestMethod]
    public void DisposingSessionInvalidatesQueuedScan()
    {
        using var fixture = new Fixture();
        fixture.Session.OpenImage(Fixture.First);
        fixture.Scanner.Complete(0, Fixture.First, Fixture.Second);
        Action delivery = fixture.TakeScan();
        var state = fixture.Session.State;
        fixture.Session.Dispose();
        delivery();
        Assert.AreSame(state, fixture.Session.State);
        Assert.IsTrue(fixture.Scanner.Requests[0].Token.IsCancellationRequested);
    }

    private sealed class Fixture : IDisposable
    {
        internal const string First = @"C:\images\a.jpg";
        internal const string Second = @"C:\images\b.jpg";
        private readonly BlockingCollection<Action> _posts = new();
        internal Scanner Scanner { get; } = new();
        internal Loader Loader { get; } = new();
        internal ViewerSession Session { get; }

        internal Fixture()
        {
            Session = new ViewerSession(new FolderNavigator(), Loader, Scanner, _posts.Add);
        }

        internal Action TakeScan()
        {
            Assert.IsTrue(_posts.TryTake(out Action? action, TimeSpan.FromSeconds(5)));
            return action;
        }

        internal void DeliverScan()
        {
            TakeScan()();
        }

        public void Dispose()
        {
            Session.Dispose();
            _posts.Dispose();
        }
    }

    private sealed class Scanner : IFolderScanner
    {
        internal List<(TaskCompletionSource<FolderEntry[]> Completion, CancellationToken Token)> Requests { get; } = [];

        public Task<FolderEntry[]> ScanAsync(string directoryPath, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<FolderEntry[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add((completion, cancellationToken));
            return completion.Task;
        }

        internal void Complete(int index, params string[] paths)
        {
            Requests[index].Completion.SetResult(paths.Select(path => new FolderEntry(path, 1, default, default)).ToArray());
        }
    }

    private sealed class Loader : IImageLoader
    {
        private Action<ImageLoadResult>? _completed;
        internal string Path { get; private set; } = string.Empty;
        internal int LoadCount { get; private set; }
        internal string?[] Preloads { get; private set; } = [];

        public void Load(string path, Action<ImageLoadResult> completed)
        {
            Path = path;
            LoadCount++;
            _completed = completed;
        }

        public void Preload(IEnumerable<string?> paths)
        {
            Preloads = paths.ToArray();
        }

        internal void Complete()
        {
            _completed!(new ImageLoaded(Path, new DecodedImage(1, 1, 4, new byte[4])));
        }

        internal void Fail()
        {
            _completed!(new ImageLoadFailed(Path, new InvalidDataException("Broken image")));
        }
    }
}
