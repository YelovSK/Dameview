using System.Drawing;
using Dameview.Navigation;

namespace Dameview.Viewing;

internal sealed class ViewerWorkspace : IDisposable
{
    private const int ClosedTabLimit = 10;

    private readonly Func<ViewerTab> _createTab;
    private readonly List<ClosedTab> _closedTabs = [];
    private FolderSort _sort = FolderSort.NameAscending;

    internal ViewerWorkspace(Func<ViewerTab> createTab)
    {
        _createTab = createTab;
        ActivePane = new ViewerPane(CreateTab());
        Root = ActivePane;
        AttachPane(ActivePane);
    }

    internal event Action<ViewerPane>? ActivePaneChanged;
    internal event Action<WorkspaceSplit?>? LayoutChanged;
    internal event Action? PaneRatiosChanged;
    internal event Action<ViewerPane>? PaneActiveTabChanged;
    internal event Action<ViewerPane>? PaneSessionStateChanged;
    internal event Action<ViewerPane>? PaneTabsChanged;

    internal WorkspaceNode Root { get; private set; }
    internal ViewerPane ActivePane { get; private set; }
    internal bool AutoBalancePanes { get; set; }
    internal int Count => ActivePane.Count;
    internal int ActiveIndex => ActivePane.ActiveIndex;
    internal IReadOnlyList<ViewerTab> Tabs => ActivePane.Tabs;
    internal ViewerTab ActiveTab => ActivePane.ActiveTab;
    internal ViewerSession ActiveSession => ActivePane.ActiveSession;

    internal void OpenImage(string path) => ActiveSession.OpenImage(path);
    internal void SelectImage(string path) => ActiveSession.SelectImage(path);

    internal void OpenImageInNewTab(string path)
    {
        ActivePane.AddTab(CreateTab());
        ActivePane.Tabs[^1].Session.OpenImage(path);
    }

    internal void OpenImageInNewTab(string path, WorkspaceDropTarget target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureContains(target.Pane);
        ViewerTab tab = CreateTab();
        switch (target)
        {
            case WorkspaceTabDropTarget tabTarget:
                tabTarget.Pane.InsertTab(tab, tabTarget.InsertionIndex, select: true);
                SelectPane(tabTarget.Pane);
                tab.Session.OpenImage(path);
                break;

            case WorkspacePaneDropTarget paneTarget:
                ViewerPane newPane = SplitPane(paneTarget.Pane, paneTarget.Side, tab);
                tab.Session.OpenImage(path);
                SelectPane(newPane);
                break;

            default:
                tab.Dispose();
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

    internal void DuplicateActiveTab(ViewerPane pane)
    {
        EnsureContains(pane);
        string? path = GetCurrentImagePath(pane.ActiveTab);
        ViewerTab tab = CreateTab();
        pane.AddTab(tab);
        pane.SelectTab(pane.Count - 1);
        if (path is not null)
        {
            tab.Session.OpenImage(path);
        }
    }

    internal void SelectRelativeTab(int offset) => ActivePane.SelectRelativeTab(offset);

    internal void SelectTab(int index) => ActivePane.SelectTab(index);

    internal void SelectTab(ViewerPane pane, int index)
    {
        EnsureContains(pane);
        pane.SelectTab(index);
    }

    internal bool CloseActiveTab() => CloseTab(ActivePane.ActiveIndex);

    internal bool CloseTab(int index)
    {
        return CloseTab(ActivePane, index);
    }

    internal bool CloseTab(ViewerPane pane, int index)
    {
        EnsureContains(pane);
        string? path = GetCurrentImagePath(pane.Tabs[index]);
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

        ViewerPane pane = closed.Pane is { } original && EnumeratePanes(Root).Contains(original)
            ? original
            : ActivePane;
        ViewerTab tab = CreateTab();
        pane.InsertTab(tab, Math.Min(closed.Index, pane.Count), select: true);
        SelectPane(pane);
        tab.Session.OpenImage(closed.Path);
        return true;
    }

    internal ViewerPane SplitPane(ViewerPane pane, WorkspaceSplitOrientation orientation)
    {
        EnsureContains(pane);
        string? path = GetCurrentImagePath(pane.ActiveTab);
        ViewerTab tab = CreateTab();
        ViewerPane newPane = SplitPane(pane, orientation, tab);
        if (path is not null)
        {
            tab.Session.OpenImage(path);
        }

        return newPane;
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
            static pane => pane.ActiveSession.State.DisplayedImage is { } image
                ? new SizeF(image.Representation.Width, image.Representation.Height)
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

    internal void ActivatePaneAfterClosing(ViewerPane pane)
    {
        EnsureContains(pane);
        if (ReferenceEquals(pane, ActivePane))
        {
            SelectPane(FindRemovalSuccessor(pane));
        }
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

        ViewerPane? successor = ReferenceEquals(ActivePane, pane)
            ? FindRemovalSuccessor(pane)
            : null;
        RemovePaneNode(pane);

        if (successor is not null)
        {
            ActivePane = successor;
            ActivePaneChanged?.Invoke(successor);
        }

        LayoutChanged?.Invoke(null);
        return true;
    }

    private ViewerPane FindRemovalSuccessor(ViewerPane pane)
    {
        WorkspaceSplit parent = FindParent(Root, pane)
            ?? throw new InvalidOperationException("The pane has no parent split.");
        return FindFirstPane(parent.GetSibling(pane));
    }

    internal void SetSort(FolderSort sort)
    {
        _sort = sort;
        foreach (ViewerPane pane in EnumeratePanes(Root))
        {
            foreach (ViewerTab tab in pane.Tabs)
            {
                tab.Session.SetSort(sort);
            }
        }
    }

    public void Dispose() => DisposeNode(Root);

    private ViewerTab CreateTab()
    {
        ViewerTab tab = _createTab();
        tab.Session.SetSort(_sort);
        return tab;
    }

    private ViewerPane SplitPane(
        ViewerPane pane,
        WorkspaceSplitOrientation orientation,
        ViewerTab initialTab)
    {
        (ViewerPane newPane, WorkspaceSplit split) = InsertSplit(
            pane,
            orientation,
            initialTab,
            newPaneFirst: false);
        LayoutChanged?.Invoke(split);
        PaneTabsChanged?.Invoke(newPane);
        return newPane;
    }

    private ViewerPane SplitPane(
        ViewerPane pane,
        WorkspacePaneDropSide side,
        ViewerTab initialTab)
    {
        (WorkspaceSplitOrientation orientation, bool newPaneFirst) = GetSplitPlacement(side);
        (ViewerPane newPane, WorkspaceSplit split) = InsertSplit(
            pane,
            orientation,
            initialTab,
            newPaneFirst);
        LayoutChanged?.Invoke(split);
        PaneTabsChanged?.Invoke(newPane);
        return newPane;
    }

    private (ViewerPane Pane, WorkspaceSplit Split) InsertSplit(
        ViewerPane pane,
        WorkspaceSplitOrientation orientation,
        ViewerTab initialTab,
        bool newPaneFirst)
    {
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
        targetPane.InsertTab(tab, insertionIndex, select: true);
        SelectPane(targetPane);
        if (removeSourcePane)
        {
            RemovePane(sourcePane);
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
        (WorkspaceSplitOrientation orientation, bool newPaneFirst) = GetSplitPlacement(side);
        (ViewerPane newPane, WorkspaceSplit split) = InsertSplit(
            targetPane,
            orientation,
            tab,
            newPaneFirst);
        if (removeSourcePane)
        {
            RemovePaneNode(sourcePane);
        }

        bool activePaneChanged = !ReferenceEquals(ActivePane, newPane);
        ActivePane = newPane;
        if (activePaneChanged)
        {
            ActivePaneChanged?.Invoke(newPane);
        }

        LayoutChanged?.Invoke(split);
        PaneTabsChanged?.Invoke(newPane);
    }

    private WorkspaceNode RemovePaneNode(ViewerPane pane)
    {
        WorkspaceSplit parent = FindParent(Root, pane)
            ?? throw new InvalidOperationException("The pane has no parent split.");
        WorkspaceNode sibling = parent.GetSibling(pane);
        ReplaceNode(parent, sibling);
        pane.Dispose();
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

    private static string? GetCurrentImagePath(ViewerTab tab)
    {
        ViewerSessionState state = tab.Session.State;
        return state.DisplayedImage?.Path ?? state.RequestedPath;
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
        if (!EnumeratePanes(Root).Contains(pane))
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

    private static ViewerPane FindFirstPane(WorkspaceNode node) => node switch
    {
        ViewerPane pane => pane,
        WorkspaceSplit split => FindFirstPane(split.First),
        _ => throw new InvalidOperationException($"Unsupported workspace node: {node.GetType().Name}."),
    };

    private static IEnumerable<ViewerPane> EnumeratePanes(WorkspaceNode node)
    {
        switch (node)
        {
            case ViewerPane pane:
                yield return pane;
                break;

            case WorkspaceSplit split:
                foreach (ViewerPane pane in EnumeratePanes(split.First))
                {
                    yield return pane;
                }

                foreach (ViewerPane pane in EnumeratePanes(split.Second))
                {
                    yield return pane;
                }

                break;

            default:
                throw new InvalidOperationException($"Unsupported workspace node: {node.GetType().Name}.");
        }
    }

    private static void DisposeNode(WorkspaceNode node)
    {
        switch (node)
        {
            case ViewerPane pane:
                pane.Dispose();
                break;

            case WorkspaceSplit split:
                DisposeNode(split.First);
                DisposeNode(split.Second);
                break;

            default:
                throw new InvalidOperationException($"Unsupported workspace node: {node.GetType().Name}.");
        }
    }

    private readonly record struct ClosedTab(ViewerPane? Pane, int Index, string Path);
}
