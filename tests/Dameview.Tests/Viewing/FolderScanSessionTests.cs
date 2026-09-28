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
        Assert.AreEqual(Fixture.First, fixture.Session.State.CurrentEntry!.FullName);
        Assert.AreEqual(Fixture.Second, fixture.Loader.Preloads[0]);
        fixture.Session.ShowNextImage();
        Assert.AreEqual(Fixture.Second, fixture.Loader.Path);
        Assert.AreEqual(Fixture.Second, fixture.Session.State.CurrentEntry!.FullName);
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

        fixture.Scanner.WaitForRequest(0);
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
        fixture.Scanner.WaitForRequest(0);
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
        fixture.Scanner.WaitForRequest(1);
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
    public void FlatteningRescansTheImagesFolderWithItsSubfolders()
    {
        using var fixture = new Fixture();
        fixture.Session.OpenImage(Fixture.First);
        fixture.Scanner.Complete(0, Fixture.First, Fixture.Second);
        fixture.DeliverScan();

        fixture.Session.ToggleFlattenFolder();
        fixture.Scanner.WaitForRequest(1);
        Assert.AreEqual(new FolderScope(@"C:\images", Recursive: true), fixture.Scanner.Requests[1].Scope);
        Assert.HasCount(0, fixture.Session.State.FolderEntries);
        fixture.Scanner.Complete(1, Fixture.First, Fixture.Nested);
        fixture.DeliverScan();
        Assert.HasCount(2, fixture.Session.State.FolderEntries);

        fixture.Session.OpenImage(Fixture.Nested);
        Assert.HasCount(2, fixture.Scanner.Requests);

        // Unflattening narrows to the folder of the image being viewed, not the one flattened.
        fixture.Session.ToggleFlattenFolder();
        fixture.Scanner.WaitForRequest(2);
        Assert.AreEqual(new FolderScope(@"C:\images\sub", Recursive: false), fixture.Scanner.Requests[2].Scope);
    }

    [TestMethod]
    public void OpeningAFolderShowsTheFirstImageItsScanFinds()
    {
        using var fixture = new Fixture();
        string folder = Path.GetTempPath();
        fixture.Session.OpenImage(folder);
        Assert.IsNull(fixture.Session.State.RequestedPath);
        Assert.IsTrue(fixture.Session.State.IsLoading);

        fixture.Scanner.Complete(0, Fixture.Second, Fixture.First);
        fixture.DeliverScan();
        Assert.AreEqual(Fixture.First, fixture.Loader.Path);
        Assert.AreEqual(Fixture.First, fixture.Session.State.CurrentEntry!.FullName);
    }

    [TestMethod]
    public void OpeningAFolderWithoutImagesSaysSo()
    {
        using var fixture = new Fixture();
        fixture.Session.OpenImage(Path.GetTempPath());
        fixture.Scanner.Complete(0);
        fixture.DeliverScan();

        Assert.IsFalse(fixture.Session.State.IsLoading);
        Assert.IsTrue(fixture.Session.State.IsError);
        StringAssert.Contains(fixture.Session.State.Message!, "No images");
        Assert.AreEqual(0, fixture.Loader.LoadCount);
    }

    [TestMethod]
    public void OpeningAFolderIsScanningUntilItsScanFinishes()
    {
        using var fixture = new Fixture();
        fixture.Session.OpenImage(Fixture.First);
        Assert.IsTrue(fixture.Session.State.IsScanning);

        fixture.Scanner.Complete(0, Fixture.First, Fixture.Second);
        fixture.DeliverScan();
        Assert.IsFalse(fixture.Session.State.IsScanning);
        Assert.IsGreaterThan(TimeSpan.Zero, fixture.Session.State.ScanDuration);

        fixture.Session.ToggleFlattenFolder();
        Assert.IsTrue(fixture.Session.State.FlattensFolder);
        Assert.IsTrue(fixture.Session.State.IsScanning);
    }

    [TestMethod]
    public void FirstScanReportsBatchesThatAddUp()
    {
        using var posts = new BlockingCollection<Action>();
        using var scanner = new BatchScanner();
        using var watcher = new FakeFolderWatcher();
        using var monitor = new FolderMonitor(
            scanner,
            watcher,
            new WindowSynchronizationContext(posts.Add),
            debounceMilliseconds: 0,
            progressInterval: TimeSpan.FromMilliseconds(10));
        var updates = new List<FolderUpdate>();
        monitor.Updated += updates.Add;
        monitor.Open(new FolderScope(@"C:\images", Recursive: true));

        scanner.Found.Add(new FolderEntry(Fixture.First, 1, default, default));
        Assert.IsTrue(posts.TryTake(out Action? batch, TimeSpan.FromSeconds(5)));
        batch();
        scanner.Found.Add(new FolderEntry(Fixture.Second, 1, default, default));
        scanner.Found.CompleteAdding();
        while (!updates[^1].ScanFinished)
        {
            Assert.IsTrue(posts.TryTake(out Action? next, TimeSpan.FromSeconds(5)));
            next();
        }

        Assert.AreEqual(2, updates.Sum(update => update.Entries.Length));
        Assert.IsTrue(updates.All(update => update.Appended));
        Assert.IsFalse(updates[0].ScanFinished);
        Assert.IsTrue(updates[^1].ScanFinished);
        Assert.AreEqual(Fixture.First, updates[0].Entries.Single().FullName);
    }

    [TestMethod]
    public void UnrelatedFileChangesDoNotTriggerAScan()
    {
        var scanner = new ExtensionScanner();
        using var watcher = new FakeFolderWatcher();
        using FolderMonitor monitor = OpenIdleMonitor(scanner, watcher);

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
        using FolderMonitor monitor = OpenIdleMonitor(scanner, watcher);

        watcher.RaiseRenamed(@"C:\images\pic.txt", @"C:\images\pic.jpg");
        Assert.AreEqual(2, scanner.Requests);

        watcher.RaiseRenamed(@"C:\images\pic.txt", @"C:\images\notes.txt");
        Assert.AreEqual(2, scanner.Requests);
    }

    // Changes that arrive while a scan runs wait for it, so these start from a finished scan.
    private static FolderMonitor OpenIdleMonitor(ExtensionScanner scanner, FakeFolderWatcher watcher)
    {
        var scanned = new TaskCompletionSource();
        var monitor = new FolderMonitor(scanner, watcher, new WindowSynchronizationContext(post => post()), debounceMilliseconds: 0);
        monitor.Updated += _ => scanned.TrySetResult();
        monitor.Open(new FolderScope(@"C:\images", Recursive: false));
        Assert.IsTrue(scanned.Task.Wait(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, scanner.Requests);
        return monitor;
    }

    [TestMethod]
    public void SlowWatcherDoesNotBlockOpen()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"Dameview-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var watcher = new FakeFolderWatcher { OnStart = () =>
        {
            entered.Set();
            release.Wait();
        } };
        var scanner = new ExtensionScanner();
        using var monitor = new FolderMonitor(scanner, watcher, new WindowSynchronizationContext(_ => { }));
        try
        {
            var open = Task.Run(() => monitor.Open(new FolderScope(directory, Recursive: false)));
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(open.Wait(TimeSpan.FromSeconds(1)));
            Assert.AreEqual(0, scanner.Requests);
            release.Set();
            scanner.WaitForRequests(1);
        }
        finally
        {
            release.Set();
            Directory.Delete(directory);
        }
    }

    [TestMethod]
    public void ClosingDuringWatcherStartupCancelsTheScan()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"Dameview-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int stops = 0;
        using var watcher = new FakeFolderWatcher
        {
            OnStart = () =>
            {
                entered.Set();
                release.Wait();
            },
            OnStop = () => Interlocked.Increment(ref stops),
        };
        var scanner = new ExtensionScanner();
        using var monitor = new FolderMonitor(scanner, watcher, new WindowSynchronizationContext(_ => { }));
        try
        {
            monitor.Open(new FolderScope(directory, Recursive: false));
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));
            var close = Task.Run(monitor.Close);
            Assert.IsTrue(close.Wait(TimeSpan.FromSeconds(1)));
            release.Set();
            Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref stops) >= 2, TimeSpan.FromSeconds(5)));
            Assert.AreEqual(0, scanner.Requests);
        }
        finally
        {
            release.Set();
            Directory.Delete(directory);
        }
    }

    [TestMethod]
    public void UnexpectedWatcherFailureIsReportedAndStillScans()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"Dameview-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        using var watcher = new FakeFolderWatcher
        {
            OnStart = () => throw new InvalidOperationException("Watcher failed"),
        };
        var failures = new ConcurrentQueue<Exception>();
        var scanner = new ExtensionScanner();
        using var monitor = new FolderMonitor(scanner, watcher, new WindowSynchronizationContext(_ => { }));
        monitor.WatcherFailed += failures.Enqueue;
        try
        {
            monitor.Open(new FolderScope(directory, Recursive: false));
            scanner.WaitForRequests(1);
            Assert.IsTrue(failures.TryDequeue(out Exception? failure));
            Assert.IsInstanceOfType<InvalidOperationException>(failure);
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal const string First = @"C:\images\a.jpg";
        internal const string Second = @"C:\images\b.jpg";
        internal const string Third = @"C:\images\c.jpg";
        internal const string Nested = @"C:\images\sub\d.jpg";
        private readonly BlockingCollection<Action> _posts = new();
        internal FakeFolderWatcher Watcher { get; } = new();
        internal Scanner Scanner { get; } = new();
        internal Loader Loader { get; } = new();
        internal FolderMonitor Monitor { get; }
        internal ViewerSession Session { get; }

        internal Fixture()
        {
            Monitor = new FolderMonitor(Scanner, Watcher, new WindowSynchronizationContext(_posts.Add), debounceMilliseconds: 0, progressInterval: Timeout.InfiniteTimeSpan);
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
        private int _requests;
        internal int Requests => Volatile.Read(ref _requests);

        public IEnumerable<FolderEntry> Scan(FolderScope scope, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            return [];
        }

        internal void WaitForRequests(int count) =>
            Assert.IsTrue(SpinWait.SpinUntil(() => Requests >= count, TimeSpan.FromSeconds(5)));

        public bool WouldInclude(FolderScope scope, string path)
            => Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class BatchScanner : IFolderScanner, IDisposable
    {
        internal BlockingCollection<FolderEntry> Found { get; } = [];

        public IEnumerable<FolderEntry> Scan(FolderScope scope, CancellationToken cancellationToken) =>
            Found.GetConsumingEnumerable(cancellationToken);

        public bool WouldInclude(FolderScope scope, string path) => true;

        public void Dispose() => Found.Dispose();
    }

    private sealed class Scanner : IFolderScanner
    {
        internal List<(TaskCompletionSource<FolderEntry[]> Completion, FolderScope Scope, CancellationToken Token)> Requests { get; } = [];

        public IEnumerable<FolderEntry> Scan(FolderScope scope, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<FolderEntry[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Requests)
            {
                Requests.Add((completion, scope, cancellationToken));
                Monitor.PulseAll(Requests);
            }
            return Wait(completion, cancellationToken);
        }

        private static IEnumerable<FolderEntry> Wait(
            TaskCompletionSource<FolderEntry[]> completion,
            CancellationToken cancellationToken)
        {
            foreach (FolderEntry entry in completion.Task.WaitAsync(cancellationToken).GetAwaiter().GetResult())
            {
                yield return entry;
            }
        }

        public bool WouldInclude(FolderScope scope, string path) => true;

        internal void Complete(int index, params string[] paths)
        {
            WaitForRequest(index);
            Requests[index].Completion.SetResult([.. paths.Select(path => new FolderEntry(path, 1, default, default))]);
        }

        internal void WaitForRequest(int index)
        {
            lock (Requests)
            {
                while (Requests.Count <= index)
                {
                    Assert.IsTrue(Monitor.Wait(Requests, TimeSpan.FromSeconds(5)));
                }
            }
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
    internal Action? OnStart { get; init; }
    internal Action? OnStop { get; init; }
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

    public void Start(FolderScope scope)
    {
        OnStart?.Invoke();
    }

    public void Stop()
    {
        OnStop?.Invoke();
    }

    public void Dispose()
    {
    }
}
