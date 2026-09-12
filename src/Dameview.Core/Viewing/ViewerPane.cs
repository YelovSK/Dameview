namespace Dameview.Viewing;

// Owns a non-empty tab group and its active-tab state.
internal sealed class ViewerPane : WorkspaceNode, IDisposable
{
    private readonly List<ViewerTab> _tabs = [];
    private readonly Dictionary<ViewerTab, Action> _sessionChangedHandlers = [];

    internal ViewerPane(ViewerTab initialTab)
    {
        AddTab(initialTab, notify: false);
    }

    internal event Action? ActiveTabChanged;
    internal event Action? ActiveSessionStateChanged;
    internal event Action? TabsChanged;

    internal int Count => _tabs.Count;
    internal int ActiveIndex { get; private set; }
    internal IReadOnlyList<ViewerTab> Tabs => _tabs;
    internal ViewerTab ActiveTab => _tabs[ActiveIndex];
    internal ViewerSession ActiveSession => ActiveTab.Session;

    internal void AddTab(ViewerTab tab) => AddTab(tab, notify: true);

    internal void InsertTab(ViewerTab tab, int index, bool select)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, (uint)_tabs.Count);
        if (_sessionChangedHandlers.ContainsKey(tab))
        {
            throw new ArgumentException("The tab already belongs to this pane.", nameof(tab));
        }

        ViewerTab activeTab = ActiveTab;
        AttachTab(tab);
        _tabs.Insert(index, tab);
        ActiveIndex = select ? index : _tabs.IndexOf(activeTab);
        if (select)
        {
            ActiveTabChanged?.Invoke();
        }

        TabsChanged?.Invoke();
    }

    internal ViewerTab DetachTab(ViewerTab tab, bool notify)
    {
        ArgumentNullException.ThrowIfNull(tab);
        int index = _tabs.IndexOf(tab);
        if (index < 0)
        {
            throw new ArgumentException("The tab does not belong to this pane.", nameof(tab));
        }

        ViewerTab activeTab = ActiveTab;
        UnsubscribeTab(tab);
        _tabs.RemoveAt(index);
        if (_tabs.Count > 0)
        {
            int activeIndex = _tabs.IndexOf(activeTab);
            ActiveIndex = activeIndex >= 0 ? activeIndex : Math.Min(index, _tabs.Count - 1);
        }

        if (notify)
        {
            if (!ReferenceEquals(activeTab, ActiveTab))
            {
                ActiveTabChanged?.Invoke();
            }

            TabsChanged?.Invoke();
        }

        return tab;
    }

    internal void MoveTab(ViewerTab tab, int insertionIndex)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)insertionIndex, (uint)_tabs.Count);
        int sourceIndex = _tabs.IndexOf(tab);
        if (sourceIndex < 0)
        {
            throw new ArgumentException("The tab does not belong to this pane.", nameof(tab));
        }

        if (sourceIndex < insertionIndex)
        {
            insertionIndex--;
        }

        if (sourceIndex == insertionIndex)
        {
            return;
        }

        ViewerTab activeTab = ActiveTab;
        _tabs.RemoveAt(sourceIndex);
        _tabs.Insert(insertionIndex, tab);
        ActiveIndex = _tabs.IndexOf(activeTab);
        TabsChanged?.Invoke();
    }

    internal void SelectRelativeTab(int offset)
    {
        if (_tabs.Count < 2)
        {
            return;
        }

        SelectTab((ActiveIndex + offset + _tabs.Count) % _tabs.Count);
    }

    internal void SelectTab(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_tabs.Count);
        if (ActiveIndex == index)
        {
            return;
        }

        ActiveIndex = index;
        ActiveTabChanged?.Invoke();
        TabsChanged?.Invoke();
    }

    internal bool CloseActiveTab() => CloseTab(ActiveIndex);

    internal bool CloseTab(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_tabs.Count);
        if (_tabs.Count == 1)
        {
            return false;
        }

        ViewerTab closing = DetachTab(_tabs[index], notify: true);
        closing.Dispose();
        return true;
    }

    public void Dispose()
    {
        foreach (ViewerTab tab in _tabs)
        {
            UnsubscribeTab(tab);
            tab.Dispose();
        }

        _tabs.Clear();
    }

    private void AddTab(ViewerTab tab, bool notify)
    {
        ArgumentNullException.ThrowIfNull(tab);
        AttachTab(tab);
        _tabs.Add(tab);
        if (notify)
        {
            TabsChanged?.Invoke();
        }
    }

    private void AttachTab(ViewerTab tab)
    {
        Action handler = () =>
        {
            if (ReferenceEquals(tab, ActiveTab))
            {
                ActiveSessionStateChanged?.Invoke();
            }

            TabsChanged?.Invoke();
        };
        _sessionChangedHandlers.Add(tab, handler);
        tab.Session.StateChanged += handler;
    }

    private void UnsubscribeTab(ViewerTab tab)
    {
        Action handler = _sessionChangedHandlers[tab];
        tab.Session.StateChanged -= handler;
        _sessionChangedHandlers.Remove(tab);
    }
}
