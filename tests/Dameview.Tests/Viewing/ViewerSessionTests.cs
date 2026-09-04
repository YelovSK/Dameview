using Dameview.Imaging;
using Dameview.Navigation;
using Dameview.Viewing;

namespace Dameview.Tests.Viewing;

[TestClass]
public sealed class ViewerSessionTests
{
    [TestMethod]
    public void LoadingAndFailureRetainTheDisplayedImageAndViewport()
    {
        using var files = new SessionFiles();
        var loader = new ManualImageLoader();
        using var session = CreateSession(loader);
        var states = new List<ViewerSessionState>();
        session.StateChanged += () => states.Add(session.State);

        session.OpenImage(files.First);
        Assert.IsTrue(session.State.IsLoading);
        Assert.IsNull(session.State.DisplayedImage);
        var image = new DecodedImage(1600, 1200, 6400, new byte[6400 * 1200]);
        loader.Complete(image);
        session.Viewport.SetActualSizeAt(400, 300, session.Viewport.ImageCenter);
        session.Viewport.PanBy(100, 50);
        var center = session.Viewport.Center;
        var mode = session.Viewport.Mode;
        var displayed = session.State.DisplayedImage;

        session.ShowNextImage();
        Assert.AreEqual(files.Second, session.State.RequestedPath);
        Assert.AreSame(displayed, session.State.DisplayedImage);
        loader.Fail(new InvalidDataException("Broken image"));

        Assert.IsFalse(session.State.IsLoading);
        Assert.IsTrue(session.State.IsError);
        Assert.AreSame(image, session.State.DisplayedImage!.Image);
        Assert.AreEqual(files.First, session.State.DisplayedImage.Path);
        Assert.AreEqual(center, session.Viewport.Center);
        Assert.AreEqual(mode, session.Viewport.Mode);
        Assert.AreEqual(1.0f, session.Viewport.Scale);
        Assert.AreEqual(5, states.Count);
        Assert.IsTrue(states[0].IsLoading);
        Assert.IsFalse(states[2].IsLoading);
        Assert.IsTrue(states[3].IsLoading);
        Assert.IsTrue(states[4].IsError);
    }

    [TestMethod]
    public void NavigationAdvancesBeforeCompletionAndPreloadsInTravelDirection()
    {
        using var files = new SessionFiles();
        var loader = new ManualImageLoader();
        using var session = CreateSession(loader);
        session.OpenImage(files.First);
        loader.Complete(CreateImage());
        CollectionAssert.AreEqual(new[] { files.Second, files.Third }, loader.Preloads);

        session.ShowNextImage();
        session.ShowNextImage();
        Assert.AreEqual(files.Third, session.State.RequestedPath);
        Assert.AreEqual(files.First, session.State.DisplayedImage!.Path);
        loader.Complete(CreateImage());
        Assert.AreEqual(files.Third, session.State.DisplayedImage!.Path);
        CollectionAssert.AreEqual(new[] { files.First, files.Second }, loader.Preloads);

        session.ShowPreviousImage();
        loader.Complete(CreateImage());
        Assert.AreEqual(files.Second, session.State.DisplayedImage!.Path);
        CollectionAssert.AreEqual(new[] { files.First, files.Third }, loader.Preloads);
        Assert.AreEqual(ViewportMode.Fit, session.Viewport.Mode);
    }

    [TestMethod]
    public void SuccessfulLoadAfterFailureClearsTheErrorAndResetsTheViewport()
    {
        using var files = new SessionFiles();
        var loader = new ManualImageLoader();
        using var session = CreateSession(loader);
        session.OpenImage(files.First);
        loader.Fail(new InvalidDataException("Broken image"));
        session.ShowNextImage();
        Assert.IsFalse(session.State.IsError);
        loader.Complete(CreateImage());

        Assert.IsNull(session.State.Message);
        Assert.IsFalse(session.State.IsLoading);
        Assert.IsFalse(session.State.IsError);
        Assert.AreEqual(files.Second, session.State.DisplayedImage!.Path);
        Assert.AreEqual(ViewportMode.Fit, session.Viewport.Mode);
    }

    [TestMethod]
    public void FolderDiscoveryFailureStillAllowsTheImageToLoad()
    {
        var loader = new ManualImageLoader();
        using var session = CreateSession(loader);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "image.jpg");
        session.OpenImage(path);
        loader.Complete(CreateImage());

        Assert.AreEqual(path, session.State.DisplayedImage!.Path);
        Assert.IsFalse(session.State.IsLoading);
        Assert.IsTrue(session.State.IsError);
        StringAssert.StartsWith(session.State.Message!, "Image opened, but its folder could not be read:");
        Assert.HasCount(0, loader.Preloads);
    }

    [TestMethod]
    public void PreviewKeepsLoadingUntilFullImageArrives()
    {
        using var files = new SessionFiles();
        var loader = new ManualImageLoader();
        using var session = CreateSession(loader);
        session.OpenImage(files.First);
        loader.Preview(CreateImage());
        Assert.IsTrue(session.State.IsLoading);
        Assert.IsTrue(session.State.DisplayedImage!.IsPreview);
        Assert.HasCount(0, loader.Preloads);
        loader.Complete(CreateImage());
        Assert.IsFalse(session.State.IsLoading);
        Assert.IsFalse(session.State.DisplayedImage!.IsPreview);
        Assert.IsNull(session.State.Message);
        Assert.AreEqual(ViewportMode.Fit, session.Viewport.Mode);
    }

    private static ViewerSession CreateSession(ManualImageLoader loader)
    {
        return new ViewerSession(new FolderNavigator(), loader, new ImmediateScanner(), action => action(), 800, 600);
    }

    private sealed class ImmediateScanner : IFolderScanner
    {
        public Task<FolderEntry[]> ScanAsync(string directoryPath, CancellationToken cancellationToken)
        {
            try
            {
                return Task.FromResult(new DirectoryInfo(directoryPath).GetFiles()
                    .Select(file => new FolderEntry(file.FullName, file.Length,
                        file.CreationTimeUtc, file.LastWriteTimeUtc)).ToArray());
            }
            catch (IOException exception)
            {
                return Task.FromException<FolderEntry[]>(exception);
            }
        }
    }

    private static DecodedImage CreateImage()
    {
        return new DecodedImage(1, 1, 4, new byte[4]);
    }

    // Matches the loader contract: a new request supersedes the old callback.
    private sealed class ManualImageLoader : IImageLoader
    {
        private string _path = string.Empty;
        private Action<ImageLoadResult>? _completed;
        internal string?[] Preloads { get; private set; } = [];

        public void Load(string path, Action<ImageLoadResult> completed)
        {
            _path = path;
            _completed = completed;
        }

        public void Preload(IEnumerable<string?> paths)
        {
            Preloads = paths.ToArray();
        }

        internal void Preview(DecodedImage image)
        {
            _completed!(new ImageLoaded(_path, image, IsPreview: true));
        }

        internal void Complete(DecodedImage image)
        {
            _completed!(new ImageLoaded(_path, image));
        }

        internal void Fail(Exception exception)
        {
            _completed!(new ImageLoadFailed(_path, exception));
        }
    }

    private sealed class SessionFiles : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("Dameview.Session.Tests.").FullName;
        internal string First => Path.Combine(_directory, "a.jpg");
        internal string Second => Path.Combine(_directory, "b.jpg");
        internal string Third => Path.Combine(_directory, "c.jpg");

        internal SessionFiles()
        {
            File.WriteAllBytes(First, []);
            File.WriteAllBytes(Second, []);
            File.WriteAllBytes(Third, []);
        }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
