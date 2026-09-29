namespace Dameview.Commands;

/// <summary>Everything the UI can ask of the application.</summary>
internal interface IAppActions : ICommandRunner, IViewerActions, ISettingsActions;
