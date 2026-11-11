namespace Dameview.Viewing;

internal closed record WorkspaceDropTarget(ViewerPane Pane);

internal sealed record WorkspaceTabDropTarget(ViewerPane Pane, int InsertionIndex)
    : WorkspaceDropTarget(Pane);

internal enum WorkspacePaneDropSide
{
    Left,
    Top,
    Right,
    Bottom,
}

internal sealed record WorkspacePaneDropTarget(
    ViewerPane Pane,
    WorkspacePaneDropSide Side)
    : WorkspaceDropTarget(Pane);
