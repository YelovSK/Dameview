using Dameview.Imaging;
using Dameview.Navigation;
using Dameview.Viewing;

namespace Dameview.Tests.Viewing;

[TestClass]
public sealed class ViewerWorkspaceTests
{
    [TestMethod]
    public void NewTabKeepsTheExistingSessionActive()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerSession first = workspace.ActiveSession;
        first.Viewport.SetViewportSize(800, 600);
        first.Viewport.SetImageSize(1600, 1200);
        first.Viewport.SetActualSizeAt(400, 300, first.Viewport.ImageCenter);

        workspace.OpenImageInNewTab(@"C:\second\image.png");

        Assert.AreEqual(2, workspace.Count);
        Assert.AreSame(first, workspace.ActiveSession);
        Assert.AreEqual(@"C:\second\image.png", workspace.Tabs[1].Session.State.RequestedPath);

        workspace.SelectRelativeTab(1);

        Assert.AreSame(workspace.Tabs[1].Session, workspace.ActiveSession);
        workspace.SelectRelativeTab(-1);
        Assert.AreEqual(ViewportMode.ActualSize, first.Viewport.Mode);
    }

    [TestMethod]
    public void TabSelectionWrapsInBothDirections()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerSession first = workspace.ActiveSession;
        workspace.OpenImageInNewTab(@"C:\second\image.png");
        ViewerSession second = workspace.Tabs[1].Session;

        workspace.SelectRelativeTab(-1);
        Assert.AreSame(second, workspace.ActiveSession);

        workspace.SelectRelativeTab(1);
        Assert.AreSame(first, workspace.ActiveSession);
    }

    [TestMethod]
    public void TabsKeepIndependentGalleryScrollState()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerTab first = workspace.ActiveTab;
        first.GalleryState.ScrollOffset.SetMaximum(500.0f);
        first.GalleryState.ScrollOffset.SetImmediate(240.0f);

        workspace.OpenImageInNewTab(@"C:\second\image.png");
        ViewerTab second = workspace.Tabs[1];
        second.GalleryState.ScrollOffset.SetMaximum(500.0f);
        second.GalleryState.ScrollOffset.SetImmediate(80.0f);

        workspace.SelectTab(1);
        Assert.AreEqual(80.0f, workspace.ActiveTab.GalleryState.ScrollOffset.Offset);

        workspace.SelectTab(0);
        Assert.AreEqual(240.0f, workspace.ActiveTab.GalleryState.ScrollOffset.Offset);
    }

    [TestMethod]
    public void ClosingTheActiveTabSelectsTheRemainingTab()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerSession first = workspace.ActiveSession;
        workspace.OpenImageInNewTab(@"C:\second\image.png");
        workspace.SelectTab(1);

        Assert.IsTrue(workspace.CloseActiveTab());
        Assert.AreEqual(1, workspace.Count);
        Assert.AreSame(first, workspace.ActiveSession);
        Assert.IsFalse(workspace.CloseActiveTab());
    }

    [TestMethod]
    public void ClosingAnEarlierTabKeepsTheActiveSession()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        workspace.OpenImageInNewTab(@"C:\second\image.png");
        ViewerSession second = workspace.Tabs[1].Session;
        workspace.OpenImageInNewTab(@"C:\third\image.png");
        workspace.SelectTab(1);

        Assert.IsTrue(workspace.CloseTab(0));

        Assert.AreEqual(2, workspace.Count);
        Assert.AreEqual(0, workspace.ActiveIndex);
        Assert.AreSame(second, workspace.ActiveSession);
    }

    [TestMethod]
    public void OnlyTheActiveSessionForwardsStateChanges()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerSession first = workspace.ActiveSession;
        workspace.OpenImageInNewTab(@"C:\second\image.png");
        workspace.SelectTab(1);
        int changes = 0;
        workspace.ActiveSessionStateChanged += () => changes++;

        first.OpenImage(@"C:\first\other.png");
        Assert.AreEqual(0, changes);

        workspace.ActiveSession.OpenImage(@"C:\second\other.png");
        Assert.AreEqual(1, changes);
    }

    private static ViewerTab CreateTab()
    {
        var monitor = new SilentFolderMonitor();
        var session = new ViewerSession(new FolderNavigator(), monitor, new SilentImageLoader());
        return new ViewerTab(session);
    }

    private sealed class SilentImageLoader : IImageLoader
    {
        public void Load(string path, Action<ImageLoadResult> completed)
        {
        }

        public void Preload(IEnumerable<string?> paths)
        {
        }
    }

    private sealed class SilentFolderMonitor : IFolderMonitor
    {
        public event Action<FolderUpdate>? Updated
        {
            add { }
            remove { }
        }
        public string? CurrentDirectory { get; private set; }

        public void Open(string directoryPath) => CurrentDirectory = directoryPath;
        public void Close() => CurrentDirectory = null;
        public void Dispose() => Close();
    }
}
