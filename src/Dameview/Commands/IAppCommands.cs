namespace Dameview.Commands;

internal interface IAppCommands : IViewerCommands, ISettingsCommands
{
    public void ExecuteCommand(ViewerCommandId command);
}
