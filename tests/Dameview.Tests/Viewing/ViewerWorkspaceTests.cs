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
    public void DuplicatingTheActiveTabSelectsAFreshSessionWithTheSameImage()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane pane = workspace.ActivePane;
        ViewerSession original = pane.ActiveSession;
        original.OpenImage(@"C:\first\image.png");

        workspace.DuplicateActiveTab(pane);

        Assert.AreEqual(2, pane.Count);
        Assert.AreEqual(1, pane.ActiveIndex);
        Assert.AreNotSame(original, pane.ActiveSession);
        Assert.AreEqual(@"C:\first\image.png", pane.ActiveSession.State.RequestedPath);
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
        workspace.PaneSessionStateChanged += _ => changes++;

        first.OpenImage(@"C:\first\other.png");
        Assert.AreEqual(0, changes);

        workspace.ActiveSession.OpenImage(@"C:\second\other.png");
        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public void SplittingAPaneBuildsTheTreeAndCopiesTheCurrentImage()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane first = workspace.ActivePane;
        first.ActiveSession.OpenImage(@"C:\first\image.png");

        ViewerPane second = workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);

        WorkspaceSplit split = Assert.IsInstanceOfType<WorkspaceSplit>(workspace.Root);
        Assert.AreEqual(WorkspaceSplitOrientation.Horizontal, split.Orientation);
        Assert.AreEqual(0.5f, split.Ratio);
        Assert.AreSame(first, split.First);
        Assert.AreSame(second, split.Second);
        Assert.AreSame(first, workspace.ActivePane);
        Assert.AreNotSame(first.ActiveSession, second.ActiveSession);
        Assert.AreEqual(first.ActiveSession.State.RequestedPath, second.ActiveSession.State.RequestedPath);
    }

    [TestMethod]
    public void SplittingANestedPaneCreatesARecursiveLayout()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane first = workspace.ActivePane;
        ViewerPane second = workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);

        ViewerPane third = workspace.SplitPane(second, WorkspaceSplitOrientation.Vertical);

        WorkspaceSplit root = Assert.IsInstanceOfType<WorkspaceSplit>(workspace.Root);
        WorkspaceSplit nested = Assert.IsInstanceOfType<WorkspaceSplit>(root.Second);
        Assert.AreSame(first, root.First);
        Assert.AreSame(second, nested.First);
        Assert.AreSame(third, nested.Second);
        Assert.AreEqual(WorkspaceSplitOrientation.Vertical, nested.Orientation);
    }

    [TestMethod]
    public void LayoutChangesIdentifyOnlyANewlyOpeningSplit()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        var changes = new List<WorkspaceSplit?>();
        workspace.LayoutChanged += changes.Add;

        ViewerPane second = workspace.SplitPane(
            workspace.ActivePane,
            WorkspaceSplitOrientation.Horizontal);

        Assert.HasCount(1, changes);
        Assert.AreSame(workspace.Root, changes[0]);

        Assert.IsTrue(workspace.RemovePane(second));
        Assert.HasCount(2, changes);
        Assert.IsNull(changes[1]);
    }

    [TestMethod]
    public void EqualizingPanesWeightsEverySplitByItsDescendantPaneCount()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane first = workspace.ActivePane;
        ViewerPane second = workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);
        ViewerPane third = workspace.SplitPane(first, WorkspaceSplitOrientation.Vertical);
        workspace.SplitPane(first, WorkspaceSplitOrientation.Vertical);
        var changes = new List<WorkspaceSplit?>();
        workspace.LayoutChanged += changes.Add;

        workspace.EqualizePanes();

        WorkspaceSplit root = Assert.IsInstanceOfType<WorkspaceSplit>(workspace.Root);
        WorkspaceSplit left = Assert.IsInstanceOfType<WorkspaceSplit>(root.First);
        WorkspaceSplit nestedLeft = Assert.IsInstanceOfType<WorkspaceSplit>(left.First);
        Assert.AreSame(second, root.Second);
        Assert.AreSame(third, left.Second);
        Assert.AreEqual(0.75f, root.Ratio);
        Assert.AreEqual(2.0f / 3.0f, left.Ratio);
        Assert.AreEqual(0.5f, nestedLeft.Ratio);
        CollectionAssert.AreEqual(new WorkspaceSplit?[] { null }, changes);
    }

    [TestMethod]
    public void EqualizingOnePaneDoesNotRaiseALayoutChange()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        int changes = 0;
        workspace.LayoutChanged += _ => changes++;

        workspace.EqualizePanes();

        Assert.AreEqual(0, changes);
    }

    [TestMethod]
    public void RemovingAnInactivePaneCollapsesItsParentAndPreservesFocus()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane first = workspace.ActivePane;
        ViewerPane second = workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);
        ViewerSession secondSession = second.ActiveSession;

        Assert.IsTrue(workspace.RemovePane(second));

        Assert.AreSame(first, workspace.Root);
        Assert.AreSame(first, workspace.ActivePane);
        Assert.ThrowsExactly<ObjectDisposedException>(() => secondSession.OpenImage(@"C:\second\other.png"));
        Assert.IsFalse(workspace.RemovePane(first));
    }

    [TestMethod]
    public void SessionStateChangesIdentifyTheirPane()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane first = workspace.ActivePane;
        ViewerPane second = workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);
        var changedPanes = new List<ViewerPane>();
        workspace.PaneSessionStateChanged += changedPanes.Add;

        second.ActiveSession.OpenImage(@"C:\second\other.png");
        first.ActiveSession.OpenImage(@"C:\first\other.png");

        CollectionAssert.AreEqual(new[] { second, first }, changedPanes);
    }

    [TestMethod]
    public void TabChangesIdentifyTheirPane()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane second = workspace.SplitPane(workspace.ActivePane, WorkspaceSplitOrientation.Horizontal);
        var activeTabChanges = new List<ViewerPane>();
        var tabChanges = new List<ViewerPane>();
        workspace.PaneActiveTabChanged += activeTabChanges.Add;
        workspace.PaneTabsChanged += tabChanges.Add;

        second.AddTab(CreateTab());
        workspace.SelectTab(second, 1);

        CollectionAssert.AreEqual(new[] { second }, activeTabChanges);
        CollectionAssert.AreEqual(new[] { second, second }, tabChanges);
    }

    [TestMethod]
    public void RemovingTheActivePaneFocusesItsSibling()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane first = workspace.ActivePane;
        ViewerPane second = workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);
        workspace.SelectPane(second);
        int focusChanges = 0;
        workspace.ActivePaneChanged += _ => focusChanges++;

        Assert.IsTrue(workspace.RemovePane(second));

        Assert.AreSame(first, workspace.Root);
        Assert.AreSame(first, workspace.ActivePane);
        Assert.AreEqual(1, focusChanges);
    }

    [TestMethod]
    public void ClosingTheOnlyTabInTheActivePaneCollapsesThatPane()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane first = workspace.ActivePane;
        ViewerPane second = workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);
        workspace.SelectPane(second);

        Assert.IsTrue(workspace.CloseActiveTab());

        Assert.AreSame(first, workspace.Root);
        Assert.AreSame(first, workspace.ActivePane);
        Assert.IsFalse(workspace.CloseActiveTab());
    }

    [TestMethod]
    public void DisposingTheWorkspaceDisposesEveryPaneRecursively()
    {
        var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane first = workspace.ActivePane;
        ViewerPane second = workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);
        ViewerPane third = workspace.SplitPane(second, WorkspaceSplitOrientation.Vertical);
        ViewerSession firstSession = first.ActiveSession;
        ViewerSession secondSession = second.ActiveSession;
        ViewerSession thirdSession = third.ActiveSession;

        workspace.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => firstSession.OpenImage(@"C:\first\other.png"));
        Assert.ThrowsExactly<ObjectDisposedException>(() => secondSession.OpenImage(@"C:\second\other.png"));
        Assert.ThrowsExactly<ObjectDisposedException>(() => thirdSession.OpenImage(@"C:\third\other.png"));
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
