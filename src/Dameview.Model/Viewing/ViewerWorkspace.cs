using System.Drawing;
using Dameview.Navigation;

namespace Dameview.Viewing;

internal sealed class ViewerWorkspace : IDisposable
{
    private const int ClosedTabLimit = 10;

    private readonly Func<ViewerTab> _createTab;
    private readonly List<ClosedTab> _closedTabs = [];
    private readonly List<ImageViewport> _shownViewports = [];
    private ViewportSync? _viewportSync;
    private FolderSort _sort = FolderSort.NameAscending;
    private double _zoomStep = ImageViewport.DefaultZoomStep;
    private int _preloadAhead = 1;

    internal ViewerWorkspace(Func<ViewerTab> createTab)
    {
        _createTab = createTab;
        ActivePane = new ViewerPane(CreateTab());
        Root = ActivePane;
        AttachPane(ActivePane);
    }

    internal event Action<ViewerPane>? ActivePaneChanged;
    /// <summary>Raised when panes are added, removed or rearranged.</summary>
    /// <remarks>A removed pane is disposed only after this returns, so listeners can still read it.</remarks>
    internal event Action<WorkspaceSplit?>? LayoutChanged;
    internal event Action? PaneRatiosChanged;
    internal event Action<ViewerPane>? PaneActiveTabChanged;
    internal event Action<ViewerPane>? PaneSessionStateChanged;
    internal event Action<ViewerPane>? PaneTabsChanged;
    internal event Action? ViewportSyncChanged;

    internal WorkspaceNode Root { get; private set; }
    internal ViewerPane ActivePane { get; private set; }
    internal bool AutoBalancePanes { get; set; }

    /// <summary>Every session in the workspace, including the tabs that are not active.</summary>
    internal IEnumerable<ViewerSession> Sessions =>
        Root.Panes.SelectMany(pane => pane.Tabs).Select(tab => tab.Session);

    internal bool IsSplit => Root is not ViewerPane;
    internal bool IsViewportSynced => _viewportSync is not null;
    internal bool HasClosedTabs => _closedTabs.Count > 0;

    internal ViewerTab ActiveTab => ActivePane.ActiveTab;
    internal ViewerSession ActiveSession => ActivePane.ActiveSession;

    internal void ToggleViewportSync()
    {
        _viewportSync = _viewportSync is null ? new ViewportSync() : null;
        ViewportSyncChanged?.Invoke();
    }

    /// <summary>Lines up the panes' views when they are synced. Called once per frame.</summary>
    internal void SyncViewports()
    {
        if (_viewportSync is null)
        {
            return;
        }

        _shownViewports.Clear();
        CollectShownViewports(Root, _shownViewports);
        _viewportSync.Sync(_shownViewports, ActiveSession.Viewport);
    }

    internal void OpenImage(string path) => ActiveSession.OpenImage(path);
    internal void SelectImage(string path) => ActiveSession.SelectImage(path);

    internal void OpenImageInNewTab(string path) => ActivePane.AddTab(CreateTab(path));

    /// <summary>Opens each image in a new tab of the active pane and selects the first of them.</summary>
    /// <returns>Whether any tab was opened.</returns>
    internal bool OpenImagesInNewTabs(IEnumerable<string> paths)
    {
        bool opened = false;
        foreach (string path in paths)
        {
            ActivePane.InsertTab(CreateTab(path), ActivePane.Count, select: !opened);
            opened = true;
        }

        return opened;
    }

    internal void OpenImageInNewTab(string path, WorkspaceDropTarget target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureContains(target.Pane);
        switch (target)
        {
            case WorkspaceTabDropTarget tabTarget:
                InsertDroppedTab(tabTarget.Pane, CreateTab(path), tabTarget.InsertionIndex);
                SelectPane(tabTarget.Pane);
                break;

            case WorkspacePaneDropTarget paneTarget:
                SelectPane(SplitPane(paneTarget.Pane, paneTarget.Side, CreateTab(path)));
                break;

            default:
                throw new ArgumentException("Unsupported workspace drop target.", nameof(target));
        }
    }

    internal bool MoveTab(ViewerPane sourcePane, ViewerTab tab, WorkspaceDropTarget target)
    {
        EnsureContains(sourcePane);
        EnsureContains(target.Pane);
        if (!sourcePane.Tabs.Contains(tab))
        {
            throw new ArgumentException("The tab does not belong to the source pane.", nameof(tab));
        }

        switch (target)
        {
            case WorkspaceTabDropTarget tabTarget:
                if (ReferenceEquals(sourcePane, tabTarget.Pane))
                {
                    sourcePane.MoveTab(tab, tabTarget.InsertionIndex);
                    return true;
                }

                MoveTabBetweenPanes(sourcePane, tab, tabTarget.Pane, tabTarget.InsertionIndex);
                return true;

            case WorkspacePaneDropTarget paneTarget:
                if (ReferenceEquals(sourcePane, paneTarget.Pane) && sourcePane.Count == 1)
                {
                    return false;
                }

                MoveTabToNewPane(sourcePane, tab, paneTarget.Pane, paneTarget.Side);
                return true;

            default:
                throw new ArgumentException("Unsupported workspace drop target.", nameof(target));
        }
    }

    internal ViewerPane PaneOf(ViewerTab tab) =>
        Root.Panes.FirstOrDefault(pane => pane.Tabs.Contains(tab))
        ?? throw new ArgumentException("The tab is not in the workspace.", nameof(tab));

    internal void DuplicateTab(ViewerTab tab)
    {
        ViewerPane pane = PaneOf(tab);
        ViewerSessionState state = tab.Session.State;
        ViewerTab copy = CreateTab();
        if (state.FlattensFolder)
        {
            copy.Session.ToggleFlattenFolder();
        }

        copy.Session.SetSortOverride(state.SortOverride);
        if (state.RequestedPath is { } path)
        {
            copy.Session.OpenImage(path);
        }

        copy.Session.SetNameFilter(state.NameFilter);

        pane.InsertTab(copy, pane.Count, select: true);
    }

    internal void SelectTab(ViewerPane pane, int index)
    {
        EnsureContains(pane);
        pane.SelectTab(index);
    }

    internal bool CloseTab(ViewerPane pane, int index)
    {
        EnsureContains(pane);
        string? path = pane.Tabs[index].Session.State.RequestedPath;
        // A pane refuses to close its last tab, so the pane goes instead and takes the tab with it.
        bool closed = pane.CloseTab(index);
        Remember(closed ? pane : null, index, path);

        return closed || RemovePane(pane);
    }

    internal bool ReopenClosedTab()
    {
        if (_closedTabs.Count == 0)
        {
            return false;
        }

        ClosedTab closed = _closedTabs[^1];
        _closedTabs.RemoveAt(_closedTabs.Count - 1);

        ViewerPane pane = closed.Pane is { } original && Root.Panes.Contains(original)
            ? original
            : ActivePane;
        pane.InsertTab(CreateTab(closed.Path), Math.Min(closed.Index, pane.Count), select: true);
        SelectPane(pane);
        return true;
    }

    internal ViewerPane SplitPane(ViewerPane pane, WorkspaceSplitOrientation orientation)
    {
        EnsureContains(pane);
        WorkspacePaneDropSide side = orientation == WorkspaceSplitOrientation.Horizontal
            ? WorkspacePaneDropSide.Right
            : WorkspacePaneDropSide.Bottom;
        return SplitPane(pane, side, CreateTab(pane.ActiveSession.State.RequestedPath));
    }

    internal void BalancePanes()
    {
        (_, bool changed) = BalanceSubtree(Root);
        if (changed)
        {
            PaneRatiosChanged?.Invoke();
        }
    }

    internal void OptimizePaneLayout(PaneLayoutArea area)
    {
        WorkspaceNode optimized = PaneLayoutOptimizer.Optimize(
            Root,
            area,
            static pane => pane.ActiveSession.State.DisplayedImage is not null
                ? pane.ActiveSession.Viewport.ImageSize
                : SizeF.Empty);

        if (HasSameTopology(Root, optimized))
        {
            if (ApplyRatios(Root, optimized))
            {
                PaneRatiosChanged?.Invoke();
            }

            return;
        }

        Root = optimized;
        LayoutChanged?.Invoke(null);
    }

    internal void SelectPane(ViewerPane pane)
    {
        EnsureContains(pane);
        if (ReferenceEquals(pane, ActivePane))
        {
            return;
        }

        ActivePane = pane;
        ActivePaneChanged?.Invoke(pane);
    }

    private static (int PaneCount, bool Changed) BalanceSubtree(WorkspaceNode node)
    {
        if (node is not WorkspaceSplit split)
        {
            return (1, false);
        }

        (int firstPaneCount, bool firstChanged) = BalanceSubtree(split.First);
        (int secondPaneCount, bool secondChanged) = BalanceSubtree(split.Second);
        int paneCount = firstPaneCount + secondPaneCount;
        float ratio = (float)firstPaneCount / paneCount;
        bool changed = split.Ratio != ratio;
        if (changed)
        {
            split.SetRatio(ratio);
        }

        return (paneCount, changed || firstChanged || secondChanged);
    }

    private static bool HasSameTopology(WorkspaceNode current, WorkspaceNode replacement)
    {
        if (current is ViewerPane currentPane && replacement is ViewerPane replacementPane)
        {
            return ReferenceEquals(currentPane, replacementPane);
        }

        return current is WorkspaceSplit currentSplit
            && replacement is WorkspaceSplit replacementSplit
            && currentSplit.Orientation == replacementSplit.Orientation
            && HasSameTopology(currentSplit.First, replacementSplit.First)
            && HasSameTopology(currentSplit.Second, replacementSplit.Second);
    }

    private static bool ApplyRatios(WorkspaceNode current, WorkspaceNode replacement)
    {
        if (current is not WorkspaceSplit currentSplit
            || replacement is not WorkspaceSplit replacementSplit)
        {
            return false;
        }

        bool changed = currentSplit.Ratio != replacementSplit.Ratio;
        if (changed)
        {
            currentSplit.SetRatio(replacementSplit.Ratio);
        }

        return ApplyRatios(currentSplit.First, replacementSplit.First)
            | ApplyRatios(currentSplit.Second, replacementSplit.Second)
            | changed;
    }

    internal bool RemovePane(ViewerPane pane)
    {
        EnsureContains(pane);
        if (ReferenceEquals(Root, pane))
        {
            return false;
        }

        bool wasActive = ReferenceEquals(ActivePane, pane);
        WorkspaceNode sibling = RemovePaneNode(pane);
        if (wasActive)
        {
            SelectPane(sibling.FirstPane);
        }

        LayoutChanged?.Invoke(null);
        pane.Dispose();
        return true;
    }

    internal void SetSort(FolderSort sort)
    {
        _sort = sort;
        foreach (ViewerSession session in Sessions)
        {
            session.SetDefaultSort(sort);
        }
    }

    /// <param name="zoomStep">The factor one wheel notch zooms by, in every tab.</param>
    internal void SetZoomStep(double zoomStep)
    {
        _zoomStep = zoomStep;
        foreach (ViewerSession session in Sessions)
        {
            session.Viewport.ZoomStep = zoomStep;
        }
    }

    /// <param name="count">How many images every tab preloads in the direction of browsing.</param>
    internal void SetPreloadAhead(int count)
    {
        if (_preloadAhead == count)
        {
            return;
        }

        _preloadAhead = count;
        foreach (ViewerSession session in Sessions)
        {
            session.SetPreloadAhead(count);
        }
    }

    public void Dispose()
    {
        foreach (ViewerPane pane in Root.Panes)
        {
            pane.Dispose();
        }
    }

    private ViewerTab CreateTab(string? path = null)
    {
        ViewerTab tab = _createTab();
        tab.Session.SetDefaultSort(_sort);
        tab.Session.Viewport.ZoomStep = _zoomStep;
        tab.Session.SetPreloadAhead(_preloadAhead);
        if (path is not null)
        {
            tab.Session.OpenImage(path);
        }

        return tab;
    }

    private ViewerPane SplitPane(ViewerPane pane, WorkspacePaneDropSide side, ViewerTab initialTab)
    {
        (ViewerPane newPane, WorkspaceSplit split) = InsertSplit(pane, side, initialTab);
        LayoutChanged?.Invoke(split);
        return newPane;
    }

    private (ViewerPane Pane, WorkspaceSplit Split) InsertSplit(
        ViewerPane pane,
        WorkspacePaneDropSide side,
        ViewerTab initialTab)
    {
        (WorkspaceSplitOrientation orientation, bool newPaneFirst) = GetSplitPlacement(side);
        var newPane = new ViewerPane(initialTab);
        AttachPane(newPane);
        var split = new WorkspaceSplit(
            orientation,
            newPaneFirst ? newPane : pane,
            newPaneFirst ? pane : newPane);
        ReplaceNode(pane, split);
        Balance();

        return (newPane, split);
    }

    private void MoveTabBetweenPanes(
        ViewerPane sourcePane,
        ViewerTab tab,
        ViewerPane targetPane,
        int insertionIndex)
    {
        bool removeSourcePane = sourcePane.Count == 1;
        sourcePane.DetachTab(tab, notify: !removeSourcePane);
        InsertDroppedTab(targetPane, tab, insertionIndex);
        SelectPane(targetPane);
        if (removeSourcePane)
        {
            RemovePane(sourcePane);
        }
    }

    // An empty pane's blank tab is only a placeholder, so a tab dropped into the pane replaces it.
    private static void InsertDroppedTab(ViewerPane pane, ViewerTab tab, int insertionIndex)
    {
        ViewerTab? placeholder = pane.IsEmpty ? pane.ActiveTab : null;
        pane.InsertTab(tab, insertionIndex, select: true);
        if (placeholder is not null)
        {
            pane.DetachTab(placeholder, notify: true);
            placeholder.Dispose();
        }
    }

    private void MoveTabToNewPane(
        ViewerPane sourcePane,
        ViewerTab tab,
        ViewerPane targetPane,
        WorkspacePaneDropSide side)
    {
        bool removeSourcePane = sourcePane.Count == 1;
        sourcePane.DetachTab(tab, notify: !removeSourcePane);
        (ViewerPane newPane, WorkspaceSplit split) = InsertSplit(targetPane, side, tab);
        if (removeSourcePane)
        {
            RemovePaneNode(sourcePane);
        }

        SelectPane(newPane);
        LayoutChanged?.Invoke(split);
        if (removeSourcePane)
        {
            sourcePane.Dispose();
        }
    }

    private WorkspaceNode RemovePaneNode(ViewerPane pane)
    {
        WorkspaceSplit parent = FindParent(Root, pane)
            ?? throw new InvalidOperationException("The pane has no parent split.");
        WorkspaceNode sibling = parent.GetSibling(pane);
        ReplaceNode(parent, sibling);
        Balance();
        return sibling;
    }

    // Callers go on to raise LayoutChanged, which rebuilds from Root, so the new
    // ratios need no notification of their own.
    private void Balance()
    {
        if (AutoBalancePanes)
        {
            BalanceSubtree(Root);
        }
    }

    private static (WorkspaceSplitOrientation Orientation, bool NewPaneFirst) GetSplitPlacement(
        WorkspacePaneDropSide side)
    {
        return side switch
        {
            WorkspacePaneDropSide.Left => (WorkspaceSplitOrientation.Horizontal, true),
            WorkspacePaneDropSide.Top => (WorkspaceSplitOrientation.Vertical, true),
            WorkspacePaneDropSide.Right => (WorkspaceSplitOrientation.Horizontal, false),
            WorkspacePaneDropSide.Bottom => (WorkspaceSplitOrientation.Vertical, false),
            _ => throw new ArgumentOutOfRangeException(nameof(side)),
        };
    }

    // A null pane no longer exists, so that tab reopens in whichever pane is active by then.
    private void Remember(ViewerPane? pane, int index, string? path)
    {
        if (path is null)
        {
            return;
        }

        _closedTabs.Add(new ClosedTab(pane, index, path));
        if (_closedTabs.Count > ClosedTabLimit)
        {
            _closedTabs.RemoveAt(0);
        }
    }

    private void AttachPane(ViewerPane pane)
    {
        pane.ActiveTabChanged += () => PaneActiveTabChanged?.Invoke(pane);
        pane.ActiveSessionStateChanged += () => PaneSessionStateChanged?.Invoke(pane);
        pane.TabsChanged += () => PaneTabsChanged?.Invoke(pane);
    }

    private void EnsureContains(ViewerPane pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        if (!Root.Panes.Contains(pane))
        {
            throw new ArgumentException("The pane does not belong to this workspace.", nameof(pane));
        }
    }

    private void ReplaceNode(WorkspaceNode node, WorkspaceNode replacement)
    {
        if (ReferenceEquals(Root, node))
        {
            Root = replacement;
            return;
        }

        WorkspaceSplit parent = FindParent(Root, node)
            ?? throw new InvalidOperationException("The node does not belong to this workspace.");
        if (!parent.ReplaceChild(node, replacement))
        {
            throw new InvalidOperationException("The parent does not contain the node.");
        }
    }

    private static WorkspaceSplit? FindParent(WorkspaceNode current, WorkspaceNode child)
    {
        if (current is not WorkspaceSplit split)
        {
            return null;
        }

        if (ReferenceEquals(split.First, child) || ReferenceEquals(split.Second, child))
        {
            return split;
        }

        return FindParent(split.First, child) ?? FindParent(split.Second, child);
    }

    // A loop rather than Panes, because it runs every frame.
    private static void CollectShownViewports(WorkspaceNode node, List<ImageViewport> viewports)
    {
        if (node is WorkspaceSplit split)
        {
            CollectShownViewports(split.First, viewports);
            CollectShownViewports(split.Second, viewports);
        }
        else if (node is ViewerPane pane)
        {
            viewports.Add(pane.ActiveSession.Viewport);
        }
    }

    private readonly record struct ClosedTab(ViewerPane? Pane, int Index, string Path);
}
