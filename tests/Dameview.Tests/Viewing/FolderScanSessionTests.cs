using System.Collections.Concurrent;
using Dameview.Imaging;
using Dameview.Imaging.Loading;
using Dameview.Navigation;
using Dameview.Viewing;
using Dameview.Win32;

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
        fixture.Session.ShowNextImage();
        Assert.AreEqual(1, fixture.Loader.LoadCount);

        fixture.Scanner.Complete(0, Fixture.First, Fixture.Second);
        fixture.DeliverScan();
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
        Assert.IsFalse(fixture.Session.State.IsError);
        Assert.IsNotNull(fixture.Session.State.FolderError);
        StringAssert.Contains(fixture.Session.State.FolderError!, "Folder unavailable");
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
        ViewerSessionState state = fixture.Session.State;
        fixture.Session.Dispose();
        delivery();
        Assert.AreSame(state, fixture.Session.State);
        Assert.IsTrue(fixture.Scanner.Requests[0].Token.IsCancellationRequested);
    }

    [TestMethod]
    public void InvalidPathClearsTheFolderAndReportsAnError()
    {
        using var fixture = new Fixture();
        fixture.Session.OpenImage(Fixture.First);
        fixture.Scanner.Complete(0, Fixture.First, Fixture.Second);
        fixture.DeliverScan();
        Assert.HasCount(2, fixture.Session.State.FolderEntries);

        fixture.Session.OpenImage(string.Empty);
        Assert.IsTrue(fixture.Session.State.IsError);
        Assert.HasCount(0, fixture.Session.State.FolderEntries);

        fixture.Session.OpenImage(Fixture.First);
        Assert.IsFalse(fixture.Session.State.IsError);
        Assert.IsFalse(fixture.Scanner.Requests[1].Token.IsCancellationRequested);
    }

    [TestMethod]
    public void FolderChangeStartsANewScanThroughTheSession()
    {
        using var fixture = new Fixture();
        fixture.Session.OpenImage(Fixture.First);
        fixture.Scanner.Complete(0, Fixture.First, Fixture.Second);
        fixture.DeliverScan();

        fixture.Watcher.RaiseCreated(Fixture.Third);
        fixture.Scanner.Complete(1, Fixture.First, Fixture.Second, Fixture.Third);
        fixture.DeliverScan();

        Assert.HasCount(2, fixture.Scanner.Requests);
        Assert.HasCount(3, fixture.Session.State.FolderEntries);
    }

    [TestMethod]
    public void OpeningImageInTheWatchedFolderDoesNotRescan()
    {
        using var fixture = new Fixture();
        fixture.Session.OpenImage(Fixture.First);
        fixture.Scanner.Complete(0, Fixture.First, Fixture.Second);
        fixture.DeliverScan();

        fixture.Session.OpenImage(Fixture.Second);
        Assert.HasCount(1, fixture.Scanner.Requests);
        Assert.HasCount(2, fixture.Session.State.FolderEntries);
        Assert.AreEqual(Fixture.Second, fixture.Loader.Path);
        Assert.AreEqual(Fixture.Second, fixture.Session.State.RequestedPath);
    }

    [TestMethod]
    public void UnrelatedFileChangesDoNotTriggerAScan()
    {
        var scanner = new ExtensionScanner();
        using var watcher = new FakeFolderWatcher();
        using var monitor = new FolderMonitor(scanner, watcher, new WindowSynchronizationContext(_ => { }), debounceMilliseconds: 0);
        monitor.Open(@"C:\images");
        Assert.AreEqual(1, scanner.Requests);

        watcher.RaiseChanged(@"C:\images\notes.txt");
        Assert.AreEqual(1, scanner.Requests);

        watcher.RaiseChanged(@"C:\images\pic.jpg");
        Assert.AreEqual(2, scanner.Requests);
    }

    [TestMethod]
    public void RenameIntoOrOutOfASupportedExtensionTriggersAScan()
    {
        var scanner = new ExtensionScanner();
        using var watcher = new FakeFolderWatcher();
        using var monitor = new FolderMonitor(scanner, watcher, new WindowSynchronizationContext(_ => { }), debounceMilliseconds: 0);
        monitor.Open(@"C:\images");
        Assert.AreEqual(1, scanner.Requests);

        watcher.RaiseRenamed(@"C:\images\pic.txt", @"C:\images\pic.jpg");
        Assert.AreEqual(2, scanner.Requests);

        watcher.RaiseRenamed(@"C:\images\pic.txt", @"C:\images\notes.txt");
        Assert.AreEqual(2, scanner.Requests);
    }

    private sealed class Fixture : IDisposable
    {
        internal const string First = @"C:\images\a.jpg";
        internal const string Second = @"C:\images\b.jpg";
        internal const string Third = @"C:\images\c.jpg";
        private readonly BlockingCollection<Action> _posts = new();
        internal FakeFolderWatcher Watcher { get; } = new();
        internal Scanner Scanner { get; } = new();
        internal Loader Loader { get; } = new();
        internal FolderMonitor Monitor { get; }
        internal ViewerSession Session { get; }

        internal Fixture()
        {
            Monitor = new FolderMonitor(Scanner, Watcher, new WindowSynchronizationContext(_posts.Add), debounceMilliseconds: 0);
            Session = new ViewerSession(new FolderNavigator(), Monitor, Loader);
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
            Monitor.Dispose();
            Session.Dispose();
            _posts.Dispose();
        }
    }

    private sealed class ExtensionScanner : IFolderScanner
    {
        internal int Requests { get; private set; }

        public Task<FolderEntry[]> ScanAsync(string directoryPath, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(Array.Empty<FolderEntry>());
        }

        public bool IsProbablySupported(string path)
            => Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase);
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

        public bool IsProbablySupported(string path) => true;

        internal void Complete(int index, params string[] paths)
        {
            Requests[index].Completion.SetResult([.. paths.Select(path => new FolderEntry(path, 1, default, default))]);
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
            Preloads = [.. paths];
        }

        public void Dispose()
        {
        }

        internal void Complete()
        {
            _completed!(new ImageLoaded(
                Path,
                new DecodedImageRepresentation(new DecodedImage(1, 1, 4, new byte[4]))));
        }

        internal void Fail()
        {
            _completed!(new ImageLoadFailed(Path, new InvalidDataException("Broken image")));
        }
    }
}

internal sealed class FakeFolderWatcher : IFolderWatcher
{
    public event Action<string>? Changed;
    public event Action<string>? Created;
    public event Action<string>? Deleted;
    public event Action<string, string>? Renamed;
    public event Action? Error;

    public void RaiseChanged(string path) => Changed?.Invoke(path);
    public void RaiseCreated(string path) => Created?.Invoke(path);
    public void RaiseDeleted(string path) => Deleted?.Invoke(path);
    public void RaiseRenamed(string newPath, string oldPath) => Renamed?.Invoke(newPath, oldPath);
    public void RaiseError() => Error?.Invoke();

    public void Start(string directoryPath)
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}
