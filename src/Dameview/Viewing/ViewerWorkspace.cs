using Dameview.Navigation;

namespace Dameview.Viewing;

internal sealed class ViewerWorkspace : IDisposable
{
    private readonly Func<ViewerTab> _createTab;
    private readonly ViewerPane _pane;
    private FolderSort _sort = FolderSort.NameAscending;

    internal ViewerWorkspace(Func<ViewerTab> createTab)
    {
        _createTab = createTab;
        _pane = new ViewerPane(CreateTab());
        _pane.ActiveTabChanged += () => ActiveTabChanged?.Invoke();
        _pane.ActiveSessionStateChanged += () => ActiveSessionStateChanged?.Invoke();
        _pane.TabsChanged += () => TabsChanged?.Invoke();
    }

    internal event Action? ActiveTabChanged;
    internal event Action? ActiveSessionStateChanged;
    internal event Action? TabsChanged;

    internal int Count => _pane.Count;
    internal int ActiveIndex => _pane.ActiveIndex;
    internal IReadOnlyList<ViewerTab> Tabs => _pane.Tabs;
    internal ViewerTab ActiveTab => _pane.ActiveTab;
    internal ViewerSession ActiveSession => _pane.ActiveSession;

    internal void OpenImage(string path) => ActiveSession.OpenImage(path);
    internal void SelectImage(string path) => ActiveSession.SelectImage(path);

    internal void OpenImageInNewTab(string path)
    {
        _pane.AddTab(CreateTab());
        _pane.Tabs[^1].Session.OpenImage(path);
    }

    internal void SelectRelativeTab(int offset) => _pane.SelectRelativeTab(offset);

    internal void SelectTab(int index) => _pane.SelectTab(index);

    internal bool CloseActiveTab() => _pane.CloseActiveTab();

    internal bool CloseTab(int index) => _pane.CloseTab(index);

    internal void SetSort(FolderSort sort)
    {
        _sort = sort;
        foreach (ViewerTab tab in _pane.Tabs)
        {
            tab.Session.SetSort(sort);
        }
    }

    public void Dispose() => _pane.Dispose();

    private ViewerTab CreateTab()
    {
        ViewerTab tab = _createTab();
        tab.Session.SetSort(_sort);
        return tab;
    }
}
