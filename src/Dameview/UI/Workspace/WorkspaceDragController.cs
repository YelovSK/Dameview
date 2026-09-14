using System.Drawing;
using Dameview.Commands;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Panels;
using Dameview.Viewing;

namespace Dameview.UI.Workspace;

internal sealed class WorkspaceDragController
{
    private readonly UiElement _coordinateSpace;
    private readonly WorkspaceView _workspaceView;
    private readonly WorkspaceDragOverlay _overlay;
    private readonly IAppCommands _commands;
    private WorkspaceDragPayload? _payload;
    private WorkspaceDropTarget? _target;

    internal WorkspaceDragController(
        UiElement coordinateSpace,
        WorkspaceView workspaceView,
        WorkspaceDragOverlay overlay,
        IAppCommands commands)
    {
        _coordinateSpace = coordinateSpace;
        _workspaceView = workspaceView;
        _overlay = overlay;
        _commands = commands;
    }

    internal bool IsActive => _payload is not null;

    internal void HandleTabPointer(
        ViewerPane pane,
        int tabIndex,
        WorkspaceDragEvent input)
    {
        ViewerPaneView? paneView = _workspaceView.FindPaneView(pane);
        if (paneView is null)
        {
            Cancel();
            return;
        }

        RectangleF paneBounds = paneView.GetBoundsRelativeTo(_coordinateSpace);
        PointF point = new(input.Position.X + paneBounds.X, input.Position.Y + paneBounds.Y);
        if (input.Kind == WorkspaceDragEventKind.Started)
        {
            if ((uint)tabIndex >= (uint)pane.Count)
            {
                return;
            }

            ViewerTab tab = pane.Tabs[tabIndex];
            string label = Path.GetFileName(tab.Session.State.RequestedPath) ?? "New tab";
            Start(new TabDragPayload(pane, tab, label), point);
            return;
        }

        Continue(input.Kind, point);
    }

    internal void HandleGalleryPointer(
        GalleryPanel gallery,
        string path,
        WorkspaceDragEvent input)
    {
        RectangleF galleryBounds = gallery.GetBoundsRelativeTo(_coordinateSpace);
        PointF point = new(input.Position.X + galleryBounds.X, input.Position.Y + galleryBounds.Y);
        if (input.Kind == WorkspaceDragEventKind.Started)
        {
            Start(new ImageDragPayload(path, Path.GetFileName(path)), point);
            return;
        }

        Continue(input.Kind, point);
    }

    internal void Cancel()
    {
        _payload = null;
        _target = null;
        _overlay.Hide();
    }

    private void Start(WorkspaceDragPayload payload, PointF point)
    {
        _payload = payload;
        _overlay.Show(payload.Label);
        Update(point);
    }

    private void Continue(WorkspaceDragEventKind kind, PointF point)
    {
        if (_payload is null)
        {
            return;
        }

        if (kind == WorkspaceDragEventKind.Cancelled)
        {
            Cancel();
            return;
        }

        Update(point);
        if (kind != WorkspaceDragEventKind.Completed)
        {
            return;
        }

        WorkspaceDragPayload payload = _payload;
        WorkspaceDropTarget? target = _target;
        Cancel();
        if (target is null)
        {
            return;
        }

        switch (payload)
        {
            case TabDragPayload tab:
                _commands.MoveTab(tab.SourcePane, tab.Tab, target);
                break;

            case ImageDragPayload image:
                _commands.OpenImageInNewTab(image.Path, target);
                break;
        }
    }

    private void Update(PointF point)
    {
        _target = null;
        RectangleF paneTargetBounds = RectangleF.Empty;
        RectangleF insertionMarker = RectangleF.Empty;
        WorkspaceDragTargetKind targetKind = WorkspaceDragTargetKind.None;

        foreach (ViewerPaneView paneView in _workspaceView.PaneViews)
        {
            RectangleF paneBounds = paneView.GetBoundsRelativeTo(_coordinateSpace);
            if (!paneBounds.Contains(point))
            {
                continue;
            }

            PointF panePoint = new(point.X - paneBounds.X, point.Y - paneBounds.Y);
            int insertionIndex = paneView.GetTabInsertionIndex(panePoint);
            if (insertionIndex >= 0)
            {
                _target = new WorkspaceTabDropTarget(paneView.Pane, insertionIndex);
                insertionMarker = paneView.GetTabInsertionMarkerBounds(insertionIndex);
                insertionMarker.Offset(paneBounds.Location);
                targetKind = WorkspaceDragTargetKind.TabInsertion;
                break;
            }

            RectangleF contentBounds = paneView.ContentBounds;
            contentBounds.Offset(paneBounds.Location);
            if (!contentBounds.Contains(point) || IsInvalidSelfSplit(paneView.Pane))
            {
                break;
            }

            paneTargetBounds = contentBounds;
            WorkspaceDockTargets dockTargets = WorkspaceDragOverlay.CalculateDockTargets(contentBounds);
            if (dockTargets.Left.Contains(point))
            {
                _target = new WorkspacePaneDropTarget(
                    paneView.Pane,
                    WorkspacePaneDropSide.Left);
                targetKind = WorkspaceDragTargetKind.SplitLeft;
            }
            else if (dockTargets.Up.Contains(point))
            {
                _target = new WorkspacePaneDropTarget(
                    paneView.Pane,
                    WorkspacePaneDropSide.Top);
                targetKind = WorkspaceDragTargetKind.SplitUp;
            }
            else if (dockTargets.Right.Contains(point))
            {
                _target = new WorkspacePaneDropTarget(
                    paneView.Pane,
                    WorkspacePaneDropSide.Right);
                targetKind = WorkspaceDragTargetKind.SplitRight;
            }
            else if (dockTargets.Down.Contains(point))
            {
                _target = new WorkspacePaneDropTarget(
                    paneView.Pane,
                    WorkspacePaneDropSide.Bottom);
                targetKind = WorkspaceDragTargetKind.SplitDown;
            }

            break;
        }

        _overlay.Update(point, paneTargetBounds, insertionMarker, targetKind);
    }

    private bool IsInvalidSelfSplit(ViewerPane targetPane)
    {
        return _payload is TabDragPayload tab
            && ReferenceEquals(tab.SourcePane, targetPane)
            && targetPane.Count == 1;
    }

    private abstract record WorkspaceDragPayload(string Label);

    private sealed record TabDragPayload(ViewerPane SourcePane, ViewerTab Tab, string TabLabel)
        : WorkspaceDragPayload(TabLabel);

    private sealed record ImageDragPayload(string Path, string ImageLabel)
        : WorkspaceDragPayload(ImageLabel);
}
