using System.Drawing;
using Dameview.Imaging.Loading;
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
    public void BalancingPanesWeightsEverySplitByItsDescendantPaneCount()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane first = workspace.ActivePane;
        ViewerPane second = workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);
        ViewerPane third = workspace.SplitPane(first, WorkspaceSplitOrientation.Vertical);
        workspace.SplitPane(first, WorkspaceSplitOrientation.Vertical);
        int ratioChanges = 0;
        workspace.PaneRatiosChanged += () => ratioChanges++;

        workspace.BalancePanes();

        WorkspaceSplit root = Assert.IsInstanceOfType<WorkspaceSplit>(workspace.Root);
        WorkspaceSplit left = Assert.IsInstanceOfType<WorkspaceSplit>(root.First);
        WorkspaceSplit nestedLeft = Assert.IsInstanceOfType<WorkspaceSplit>(left.First);
        Assert.AreSame(second, root.Second);
        Assert.AreSame(third, left.Second);
        Assert.AreEqual(0.75f, root.Ratio);
        Assert.AreEqual(2.0f / 3.0f, left.Ratio);
        Assert.AreEqual(0.5f, nestedLeft.Ratio);
        Assert.AreEqual(1, ratioChanges);
    }

    // Without balancing every new split halves its own parent; with it, three panes in a
    // right-leaning chain each take a third of the width.
    [TestMethod]
    [DataRow(false, 0.5f)]
    [DataRow(true, 2.0f / 3.0f)]
    public void SplittingTwiceDividesTheWidthAccordingToAutoBalance(bool autoBalance, float expectedRoot)
    {
        using var workspace = new ViewerWorkspace(CreateTab) { AutoBalancePanes = autoBalance };
        ViewerPane first = workspace.ActivePane;
        int ratioChanges = 0;
        workspace.PaneRatiosChanged += () => ratioChanges++;
        workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);
        workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);

        WorkspaceSplit root = Assert.IsInstanceOfType<WorkspaceSplit>(workspace.Root);
        WorkspaceSplit nested = Assert.IsInstanceOfType<WorkspaceSplit>(root.First);
        Assert.AreEqual(expectedRoot, root.Ratio);
        Assert.AreEqual(0.5f, nested.Ratio);

        // LayoutChanged already rebuilds from the root, so the split does not
        // additionally announce the ratios it just rewrote.
        Assert.AreEqual(0, ratioChanges);
    }

    [TestMethod]
    public void ClosingAPaneRebalancesTheRestWhenAutoBalanceIsEnabled()
    {
        using var workspace = new ViewerWorkspace(CreateTab) { AutoBalancePanes = true };
        ViewerPane first = workspace.ActivePane;
        workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);
        ViewerPane third = workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);

        Assert.IsTrue(workspace.RemovePane(third));

        // The two survivors split the width evenly again.
        WorkspaceSplit root = Assert.IsInstanceOfType<WorkspaceSplit>(workspace.Root);
        Assert.AreEqual(0.5f, root.Ratio);
    }

    [TestMethod]
    public void ClosingAPaneKeepsTheRatiosWhenAutoBalanceIsDisabled()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane first = workspace.ActivePane;
        workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);
        ViewerPane third = workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);
        WorkspaceSplit outer = Assert.IsInstanceOfType<WorkspaceSplit>(workspace.Root);
        outer.SetRatio(0.8f);

        Assert.IsTrue(workspace.RemovePane(third));

        WorkspaceSplit root = Assert.IsInstanceOfType<WorkspaceSplit>(workspace.Root);
        Assert.AreEqual(0.8f, root.Ratio, "The chosen ratio outlives the pane that was closed.");
    }

    [TestMethod]
    public void OptimizingPaneLayoutCanChangeTheSplitOrientation()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        workspace.SplitPane(workspace.ActivePane, WorkspaceSplitOrientation.Vertical);
        var changes = new List<WorkspaceSplit?>();
        workspace.LayoutChanged += changes.Add;

        workspace.OptimizePaneLayout(LayoutArea(2000.0f, 1000.0f));

        WorkspaceSplit root = Assert.IsInstanceOfType<WorkspaceSplit>(workspace.Root);
        Assert.AreEqual(WorkspaceSplitOrientation.Horizontal, root.Orientation);
        Assert.AreEqual(0.5f, root.Ratio);
        CollectionAssert.AreEqual(new WorkspaceSplit?[] { null }, changes);
    }

    [TestMethod]
    public void OptimizedHorizontalLayoutGivesTheWiderImageMoreWidth()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane tall = workspace.ActivePane;
        ViewerPane wide = workspace.SplitPane(tall, WorkspaceSplitOrientation.Vertical);

        WorkspaceNode optimized = PaneLayoutOptimizer.Optimize(
            workspace.Root,
            LayoutArea(2500.0f, 1000.0f),
            pane => ReferenceEquals(pane, tall)
                ? new SizeF(500.0f, 1000.0f)
                : new SizeF(2000.0f, 1000.0f));

        WorkspaceSplit root = Assert.IsInstanceOfType<WorkspaceSplit>(optimized);
        Assert.AreEqual(WorkspaceSplitOrientation.Horizontal, root.Orientation);
        Assert.AreSame(tall, root.First);
        Assert.AreSame(wide, root.Second);
        Assert.AreEqual(0.2f, root.Ratio, 0.0001f);
    }

    [TestMethod]
    public void OptimizerDoesNotSacrificeOneImageForBetterSpaceUtilization()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane tall = workspace.ActivePane;
        ViewerPane wide = workspace.SplitPane(tall, WorkspaceSplitOrientation.Horizontal);

        WorkspaceNode optimized = PaneLayoutOptimizer.Optimize(
            workspace.Root,
            LayoutArea(1000.0f, 1000.0f),
            pane => ReferenceEquals(pane, tall)
                ? new SizeF(5000.0f, 10000.0f)
                : new SizeF(10000.0f, 5000.0f));

        WorkspaceSplit root = Assert.IsInstanceOfType<WorkspaceSplit>(optimized);
        Assert.AreEqual(WorkspaceSplitOrientation.Horizontal, root.Orientation);
        Assert.AreEqual(0.5f, root.Ratio, 0.01f);
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(11)]
    public void OptimizingManyPanesPreservesEveryPaneExactlyOnce(int paneCount)
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        var panes = new List<ViewerPane> { workspace.ActivePane };
        for (int index = 1; index < paneCount; index++)
        {
            panes.Add(workspace.SplitPane(
                panes[^1],
                index % 2 == 0
                    ? WorkspaceSplitOrientation.Horizontal
                    : WorkspaceSplitOrientation.Vertical));
        }

        WorkspaceNode optimized = PaneLayoutOptimizer.Optimize(
            workspace.Root,
            LayoutArea(1600.0f, 900.0f),
            pane => new SizeF(400.0f + panes.IndexOf(pane) * 350.0f, 1000.0f));

        CollectionAssert.AreEquivalent(panes, EnumeratePanes(optimized).ToArray());
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

        // Which pane raised them is the point; how many times is an implementation detail.
        CollectionAssert.AreEqual(new[] { second }, activeTabChanges);
        Assert.IsNotEmpty(tabChanges);
        Assert.IsTrue(tabChanges.All(pane => ReferenceEquals(pane, second)));
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
    public void StartingToCloseTheActivePaneFocusesItsSiblingBeforeRemoval()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane first = workspace.ActivePane;
        ViewerPane second = workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);
        workspace.SelectPane(second);
        int focusChanges = 0;
        workspace.ActivePaneChanged += _ => focusChanges++;

        workspace.ActivatePaneAfterClosing(second);

        Assert.AreSame(first, workspace.ActivePane);
        Assert.IsInstanceOfType<WorkspaceSplit>(workspace.Root);
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

    [TestMethod]
    public void ReorderingMovesTheExistingTabAndPreservesTheActiveSession()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane pane = workspace.ActivePane;
        ViewerTab first = pane.ActiveTab;
        workspace.OpenImageInNewTab(@"C:\second\image.png");
        ViewerTab second = pane.Tabs[1];
        workspace.SelectTab(pane, 1);

        Assert.IsTrue(workspace.MoveTab(
            pane,
            second,
            new WorkspaceTabDropTarget(pane, 0)));

        Assert.AreSame(second, pane.Tabs[0]);
        Assert.AreSame(first, pane.Tabs[1]);
        Assert.AreSame(second.Session, pane.ActiveSession);
    }

    [TestMethod]
    public void MovingATabBetweenPanesTransfersItsSessionWithoutDisposingIt()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane source = workspace.ActivePane;
        workspace.OpenImageInNewTab(@"C:\moved\image.png");
        ViewerTab moved = source.Tabs[1];
        ViewerPane target = workspace.SplitPane(source, WorkspaceSplitOrientation.Horizontal);

        Assert.IsTrue(workspace.MoveTab(
            source,
            moved,
            new WorkspaceTabDropTarget(target, 1)));

        Assert.AreEqual(1, source.Count);
        Assert.AreEqual(2, target.Count);
        Assert.AreSame(moved, target.ActiveTab);
        Assert.AreEqual(@"C:\moved\image.png", moved.Session.State.RequestedPath);
        int sourceChanges = 0;
        int targetChanges = 0;
        workspace.PaneTabsChanged += pane =>
        {
            sourceChanges += ReferenceEquals(pane, source) ? 1 : 0;
            targetChanges += ReferenceEquals(pane, target) ? 1 : 0;
        };
        moved.Session.OpenImage(@"C:\moved\other.png");
        Assert.AreEqual(@"C:\moved\other.png", target.ActiveSession.State.RequestedPath);
        Assert.AreEqual(0, sourceChanges);
        Assert.AreEqual(1, targetChanges);
    }

    [TestMethod]
    public void MovingTheLastTabToAnotherPaneCollapsesItsSourcePane()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane source = workspace.ActivePane;
        ViewerTab moved = source.ActiveTab;
        ViewerPane target = workspace.SplitPane(source, WorkspaceSplitOrientation.Horizontal);

        Assert.IsTrue(workspace.MoveTab(
            source,
            moved,
            new WorkspaceTabDropTarget(target, 1)));

        Assert.AreSame(target, workspace.Root);
        Assert.AreSame(target, workspace.ActivePane);
        Assert.AreSame(moved, target.ActiveTab);
    }

    [TestMethod]
    public void MovingATabOntoAPaneCreatesASplitWithThatSameTab()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane source = workspace.ActivePane;
        workspace.OpenImageInNewTab(@"C:\moved\image.png");
        ViewerTab moved = source.Tabs[1];

        Assert.IsTrue(workspace.MoveTab(
            source,
            moved,
            new WorkspacePaneDropTarget(source, WorkspacePaneDropSide.Bottom)));

        WorkspaceSplit split = Assert.IsInstanceOfType<WorkspaceSplit>(workspace.Root);
        ViewerPane target = Assert.IsInstanceOfType<ViewerPane>(split.Second);
        Assert.AreEqual(1, source.Count);
        Assert.AreSame(moved, target.ActiveTab);
        Assert.AreSame(target, workspace.ActivePane);
    }

    [TestMethod]
    public void SplittingAnotherPaneWithTheLastSourceTabClosesTheSourceInOneLayoutChange()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane source = workspace.ActivePane;
        ViewerTab moved = source.ActiveTab;
        ViewerPane target = workspace.SplitPane(source, WorkspaceSplitOrientation.Horizontal);
        int layoutChanges = 0;
        workspace.LayoutChanged += _ => layoutChanges++;

        Assert.IsTrue(workspace.MoveTab(
            source,
            moved,
            new WorkspacePaneDropTarget(target, WorkspacePaneDropSide.Bottom)));

        WorkspaceSplit split = Assert.IsInstanceOfType<WorkspaceSplit>(workspace.Root);
        ViewerPane newPane = Assert.IsInstanceOfType<ViewerPane>(split.Second);
        Assert.AreSame(target, split.First);
        Assert.AreSame(moved, newPane.ActiveTab);
        Assert.AreSame(newPane, workspace.ActivePane);
        Assert.AreEqual(1, layoutChanges);
    }

    [TestMethod]
    public void SoleTabCannotSplitItsOwnPaneByMovingItself()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane pane = workspace.ActivePane;

        Assert.IsFalse(workspace.MoveTab(
            pane,
            pane.ActiveTab,
            new WorkspacePaneDropTarget(pane, WorkspacePaneDropSide.Right)));

        Assert.AreSame(pane, workspace.Root);
        Assert.AreEqual(1, pane.Count);
    }

    [TestMethod]
    public void GalleryDropCreatesANewTabInTheRequestedPane()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane first = workspace.ActivePane;
        ViewerPane second = workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);

        workspace.OpenImageInNewTab(
            @"C:\gallery\image.png",
            new WorkspaceTabDropTarget(second, 0));

        Assert.AreEqual(2, second.Count);
        Assert.AreEqual(0, second.ActiveIndex);
        Assert.AreEqual(@"C:\gallery\image.png", second.ActiveSession.State.RequestedPath);
        Assert.AreSame(second, workspace.ActivePane);
    }

    // Left and Top put the dropped pane before the target, Right and Bottom after it.
    [TestMethod]
    // The enums are internal, so the rows carry their underlying values.
    [DataRow((int)WorkspacePaneDropSide.Left, (int)WorkspaceSplitOrientation.Horizontal, true)]
    [DataRow((int)WorkspacePaneDropSide.Right, (int)WorkspaceSplitOrientation.Horizontal, false)]
    [DataRow((int)WorkspacePaneDropSide.Top, (int)WorkspaceSplitOrientation.Vertical, true)]
    [DataRow((int)WorkspacePaneDropSide.Bottom, (int)WorkspaceSplitOrientation.Vertical, false)]
    public void ADroppedSideDecidesTheOrientationAndWhichPaneComesFirst(
        int rawSide,
        int rawOrientation,
        bool newPaneFirst)
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane target = workspace.ActivePane;

        workspace.OpenImageInNewTab(
            @"C:\gallery\image.png",
            new WorkspacePaneDropTarget(target, (WorkspacePaneDropSide)rawSide));

        WorkspaceSplit split = Assert.IsInstanceOfType<WorkspaceSplit>(workspace.Root);
        Assert.AreEqual((WorkspaceSplitOrientation)rawOrientation, split.Orientation);
        WorkspaceNode expectedNew = newPaneFirst ? split.First : split.Second;
        WorkspaceNode expectedTarget = newPaneFirst ? split.Second : split.First;
        ViewerPane newPane = Assert.IsInstanceOfType<ViewerPane>(expectedNew);
        Assert.AreEqual(@"C:\gallery\image.png", newPane.ActiveSession.State.RequestedPath);
        Assert.AreSame(expectedTarget, target);
    }

    [TestMethod]
    public void MovingATabToASideCarriesTheTabIntoTheNewPane()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane source = workspace.ActivePane;
        workspace.OpenImageInNewTab(@"C:\moved\image.png");
        ViewerTab moved = source.Tabs[1];

        Assert.IsTrue(workspace.MoveTab(
            source,
            moved,
            new WorkspacePaneDropTarget(source, WorkspacePaneDropSide.Left)));

        WorkspaceSplit split = Assert.IsInstanceOfType<WorkspaceSplit>(workspace.Root);
        ViewerPane newPane = Assert.IsInstanceOfType<ViewerPane>(split.First);
        Assert.AreSame(moved, newPane.ActiveTab);
        Assert.AreSame(source, split.Second);
    }

    [TestMethod]
    public void ReopeningRestoresTheLastClosedTabWhereItWas()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane pane = workspace.ActivePane;
        pane.ActiveSession.OpenImage(@"C:\folder\first.png");
        workspace.OpenImageInNewTab(@"C:\folder\second.png");
        workspace.OpenImageInNewTab(@"C:\folder\third.png");

        Assert.IsTrue(workspace.CloseTab(pane, 1));
        Assert.IsTrue(workspace.ReopenClosedTab());

        Assert.AreEqual(3, pane.Count);
        Assert.AreEqual(@"C:\folder\second.png", pane.Tabs[1].Session.State.RequestedPath);
        Assert.AreEqual(1, pane.ActiveIndex, "The reopened tab is the one you are now looking at.");
    }

    [TestMethod]
    public void ReopeningWorksBackwardsAndRunsOut()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane pane = workspace.ActivePane;
        pane.ActiveSession.OpenImage(@"C:\folder\first.png");
        workspace.OpenImageInNewTab(@"C:\folder\second.png");
        workspace.OpenImageInNewTab(@"C:\folder\third.png");

        Assert.IsTrue(workspace.CloseTab(pane, 2));
        Assert.IsTrue(workspace.CloseTab(pane, 1));

        Assert.IsTrue(workspace.ReopenClosedTab());
        Assert.AreEqual(@"C:\folder\second.png", pane.ActiveSession.State.RequestedPath);
        Assert.IsTrue(workspace.ReopenClosedTab());
        Assert.AreEqual(@"C:\folder\third.png", pane.ActiveSession.State.RequestedPath);

        Assert.IsFalse(workspace.ReopenClosedTab(), "Nothing is left to reopen.");
    }

    [TestMethod]
    public void OnlyTheMostRecentClosedTabsAreRemembered()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane pane = workspace.ActivePane;
        pane.ActiveSession.OpenImage(@"C:\folder\keep.png");
        for (int index = 0; index < 12; index++)
        {
            workspace.OpenImageInNewTab($@"C:\folder\{index}.png");
            Assert.IsTrue(workspace.CloseTab(pane, 1));
        }

        var reopened = new List<string?>();
        while (workspace.ReopenClosedTab())
        {
            reopened.Add(workspace.ActiveSession.State.RequestedPath);
        }

        Assert.HasCount(10, reopened);
        Assert.AreEqual(@"C:\folder\11.png", reopened[0], "Newest first.");
        Assert.AreEqual(@"C:\folder\2.png", reopened[^1], "The two oldest fell off the end.");
    }

    [TestMethod]
    public void AClosedTabWhosePaneIsGoneReopensInTheActivePane()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane first = workspace.ActivePane;
        first.ActiveSession.OpenImage(@"C:\folder\first.png");
        ViewerPane second = workspace.SplitPane(first, WorkspaceSplitOrientation.Horizontal);
        second.ActiveSession.OpenImage(@"C:\second\only.png");

        // Closing its last tab takes the pane with it.
        Assert.IsTrue(workspace.CloseTab(second, 0));
        Assert.IsInstanceOfType<ViewerPane>(workspace.Root);

        Assert.IsTrue(workspace.ReopenClosedTab());
        Assert.AreSame(first, workspace.ActivePane);
        Assert.AreEqual(@"C:\second\only.png", first.ActiveSession.State.RequestedPath);
    }

    [TestMethod]
    public void ATabThatNeverHeldAnImageIsNotWorthReopening()
    {
        using var workspace = new ViewerWorkspace(CreateTab);
        ViewerPane pane = workspace.ActivePane;
        pane.ActiveSession.OpenImage(@"C:\folder\first.png");
        pane.AddTab(CreateTab());

        Assert.IsTrue(workspace.CloseTab(pane, 1));

        Assert.IsFalse(workspace.ReopenClosedTab());
    }

    private static ViewerTab CreateTab()
    {
        var monitor = new SilentFolderMonitor();
        var session = new ViewerSession(new FolderNavigator(), monitor, new SilentImageLoader());
        return new ViewerTab(session);
    }

    private static PaneLayoutArea LayoutArea(float width, float height) =>
        new(width, height, SplitterSize: 0.0f, PaneHeaderHeight: 0.0f, MinimumPaneSize: 0.0f);

    private static IEnumerable<ViewerPane> EnumeratePanes(WorkspaceNode node)
    {
        if (node is ViewerPane pane)
        {
            yield return pane;
            yield break;
        }

        WorkspaceSplit split = Assert.IsInstanceOfType<WorkspaceSplit>(node);
        foreach (ViewerPane child in EnumeratePanes(split.First))
        {
            yield return child;
        }

        foreach (ViewerPane child in EnumeratePanes(split.Second))
        {
            yield return child;
        }
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
        public string? CurrentDirectory { get; private set; }

        public void Open(string directoryPath) => CurrentDirectory = directoryPath;
        public void Close() => CurrentDirectory = null;
        public void Dispose() => Close();
    }
}
