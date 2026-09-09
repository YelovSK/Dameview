using Dameview.Navigation;

namespace Dameview.Viewing;

internal sealed class ViewerWorkspace : IDisposable
{
    private readonly Func<ViewerTab> _createTab;
    private FolderSort _sort = FolderSort.NameAscending;

    internal ViewerWorkspace(Func<ViewerTab> createTab)
    {
        _createTab = createTab;
        ActivePane = new ViewerPane(CreateTab());
        Root = ActivePane;
        AttachPane(ActivePane);
    }

    internal event Action<ViewerPane>? ActivePaneChanged;
    internal event Action? LayoutChanged;
    internal event Action<ViewerPane>? PaneActiveTabChanged;
    internal event Action<ViewerPane>? PaneSessionStateChanged;
    internal event Action<ViewerPane>? PaneTabsChanged;

    internal WorkspaceNode Root { get; private set; }
    internal ViewerPane ActivePane { get; private set; }
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
        if (pane.CloseTab(index))
        {
            return true;
        }

        return RemovePane(pane);
    }

    internal ViewerPane SplitPane(ViewerPane pane, WorkspaceSplitOrientation orientation)
    {
        EnsureContains(pane);
        ViewerSessionState state = pane.ActiveSession.State;
        string? path = state.DisplayedImage?.Path ?? state.RequestedPath;
        var newPane = new ViewerPane(CreateTab());
        AttachPane(newPane);
        ReplaceNode(pane, new WorkspaceSplit(orientation, pane, newPane));
        LayoutChanged?.Invoke();
        if (path is not null)
        {
            newPane.ActiveSession.OpenImage(path);
        }

        return newPane;
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

    internal bool RemovePane(ViewerPane pane)
    {
        EnsureContains(pane);
        if (ReferenceEquals(Root, pane))
        {
            return false;
        }

        WorkspaceSplit parent = FindParent(Root, pane)
            ?? throw new InvalidOperationException("The pane has no parent split.");
        WorkspaceNode sibling = parent.GetSibling(pane);
        ReplaceNode(parent, sibling);

        if (ReferenceEquals(ActivePane, pane))
        {
            ActivePane = FindFirstPane(sibling);
            ActivePaneChanged?.Invoke(ActivePane);
        }

        LayoutChanged?.Invoke();
        pane.Dispose();
        return true;
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
}
