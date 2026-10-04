using Dameview.Settings;
using Dameview.UI;
using Dameview.Viewing;

namespace Dameview.Commands;

/// <summary>What commands work through.</summary>
internal interface ICommandHost
{
    public ViewerWorkspace Workspace { get; }
    public ViewerUi Ui { get; }
    public FileActions Files { get; }

    /// <summary>Closes the tab, or its whole pane when it is the last one there.</summary>
    public void CloseTab(ViewerTab tab);

    public void ToggleFullscreen();

    public void UpdateSettings(Func<AppSettings, AppSettings> change);
}
