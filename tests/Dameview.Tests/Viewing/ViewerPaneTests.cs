using Dameview.Imaging.Loading;
using Dameview.Navigation;
using Dameview.Viewing;

namespace Dameview.Tests.Viewing;

[TestClass]
public sealed class ViewerPaneTests
{
    [TestMethod]
    public void TabReportsDisposalOnce()
    {
        ViewerTab tab = CreateTab();
        int disposalCount = 0;
        tab.Disposed += _ => disposalCount++;

        tab.Dispose();
        tab.Dispose();

        Assert.AreEqual(1, disposalCount);
    }

    [TestMethod]
    public void AddedTabDoesNotReplaceTheActiveTab()
    {
        ViewerTab first = CreateTab();
        using var pane = new ViewerPane(first);
        ViewerTab second = CreateTab();

        pane.AddTab(second);

        Assert.AreEqual(2, pane.Count);
        Assert.AreSame(first, pane.ActiveTab);

        pane.SelectRelativeTab(1);

        Assert.AreSame(second, pane.ActiveTab);
        pane.SelectRelativeTab(1);
        Assert.AreSame(first, pane.ActiveTab);
    }

    [TestMethod]
    public void ClosingAnEarlierTabKeepsTheActiveTabAndDisposesTheClosedTab()
    {
        ViewerTab first = CreateTab();
        using var pane = new ViewerPane(first);
        ViewerTab second = CreateTab();
        pane.AddTab(second);
        pane.SelectTab(1);

        Assert.IsTrue(pane.CloseTab(0));

        Assert.AreEqual(1, pane.Count);
        Assert.AreEqual(0, pane.ActiveIndex);
        Assert.AreSame(second, pane.ActiveTab);
        Assert.ThrowsExactly<ObjectDisposedException>(() => first.Session.OpenImage(@"C:\first\other.png"));
        Assert.IsFalse(pane.CloseTab(pane.ActiveIndex));
    }

    [TestMethod]
    public void OnlyTheActiveSessionForwardsStateChanges()
    {
        ViewerTab first = CreateTab();
        using var pane = new ViewerPane(first);
        ViewerTab second = CreateTab();
        pane.AddTab(second);
        pane.SelectTab(1);
        int changes = 0;
        pane.ActiveSessionStateChanged += () => changes++;

        first.Session.OpenImage(@"C:\first\other.png");
        Assert.AreEqual(0, changes);

        second.Session.OpenImage(@"C:\second\other.png");
        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public void TabsChangeOnlyWhenATabsPathDoes()
    {
        ViewerTab tab = CreateTab();
        using var pane = new ViewerPane(tab);
        int stateChanges = 0;
        int tabChanges = 0;
        pane.ActiveSessionStateChanged += () => stateChanges++;
        pane.TabsChanged += () => tabChanges++;

        tab.Session.OpenImage(@"C:\first\image.png");
        Assert.AreEqual(1, tabChanges);

        int stateChangesBefore = stateChanges;
        tab.Session.OpenImage(@"C:\first\image.png");
        Assert.IsTrue(stateChanges > stateChangesBefore, "Reopening the same image still changes the session's state.");
        Assert.AreEqual(1, tabChanges);
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

        public void Dispose()
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

        public FolderScope? Scope { get; private set; }

        public void Open(FolderScope scope) => Scope = scope;
        public void Close() => Scope = null;
        public void Dispose() => Close();
    }
}
