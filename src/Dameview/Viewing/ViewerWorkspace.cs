using Dameview.Navigation;

namespace Dameview.Viewing;

internal sealed class ViewerWorkspace : IDisposable
{
    private readonly Func<ViewerTab> _createTab;
    private readonly List<ViewerTab> _tabs = [];
    private FolderSort _sort = FolderSort.NameAscending;

    internal ViewerWorkspace(Func<ViewerTab> createTab)
    {
        _createTab = createTab;
        AddTab();
    }

    internal event Action? ActiveTabChanged;
    internal event Action? ActiveSessionStateChanged;
    internal event Action? TabsChanged;

    internal int Count => _tabs.Count;
    internal int ActiveIndex { get; private set; }
    internal IReadOnlyList<ViewerTab> Tabs => _tabs;
    internal ViewerTab ActiveTab => _tabs[ActiveIndex];
    internal ViewerSession ActiveSession => ActiveTab.Session;

    internal void OpenImage(string path) => ActiveSession.OpenImage(path);
    internal void SelectImage(string path) => ActiveSession.SelectImage(path);

    internal void OpenImageInNewTab(string path)
    {
        AddTab();
        TabsChanged?.Invoke();
        _tabs[^1].Session.OpenImage(path);
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

    internal void SetSort(FolderSort sort)
    {
        _sort = sort;
        foreach (ViewerTab tab in _tabs)
        {
            tab.Session.SetSort(sort);
        }
    }

    public void Dispose()
    {
        foreach (ViewerTab tab in _tabs)
        {
            tab.Dispose();
        }

        _tabs.Clear();
    }

    private void AddTab()
    {
        ViewerTab tab = _createTab();
        tab.Session.SetSort(_sort);
        tab.Session.StateChanged += () =>
        {
            if (ReferenceEquals(tab, ActiveTab))
            {
                ActiveSessionStateChanged?.Invoke();
            }

            TabsChanged?.Invoke();
        };
        _tabs.Add(tab);
    }
}
