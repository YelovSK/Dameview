namespace Dameview.Commands;

internal interface IAppCommands : IViewerCommands, ISettingsCommands
{
    public void ExecuteCommand(ViewerCommandId command);

    /// <summary>Whether the command would do anything in the current state.</summary>
    public bool CanExecuteCommand(ViewerCommandId command);
}
