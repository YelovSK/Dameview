namespace Dameview.Viewing;

// Owns a non-empty tab group and its active-tab state.
internal sealed class ViewerPane : IDisposable
{
    private readonly List<ViewerTab> _tabs = [];

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

        ViewerTab closing = _tabs[index];
        bool activeTabChanged = index == ActiveIndex;
        _tabs.RemoveAt(index);
        if (index < ActiveIndex || ActiveIndex == _tabs.Count)
        {
            ActiveIndex--;
        }

        if (activeTabChanged)
        {
            ActiveTabChanged?.Invoke();
        }

        TabsChanged?.Invoke();
        closing.Dispose();
        return true;
    }

    public void Dispose()
    {
        foreach (ViewerTab tab in _tabs)
        {
            tab.Dispose();
        }

        _tabs.Clear();
    }

    private void AddTab(ViewerTab tab, bool notify)
    {
        ArgumentNullException.ThrowIfNull(tab);
        tab.Session.StateChanged += () =>
        {
            if (ReferenceEquals(tab, ActiveTab))
            {
                ActiveSessionStateChanged?.Invoke();
            }

            TabsChanged?.Invoke();
        };
        _tabs.Add(tab);
        if (notify)
        {
            TabsChanged?.Invoke();
        }
    }
}
