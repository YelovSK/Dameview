using System.Drawing;
using Dameview.Commands;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Panels;
using Dameview.Viewing;

namespace Dameview.UI.Workspace;

internal sealed class WorkspaceDragController
{
    // How far into a pane, as a fraction of its size, an edge still splits it.
    private const float SplitEdgeFraction = 0.35f;

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
            string? path = tab.Session.State.RequestedPath;
            Start(new TabDragPayload(pane, tab, Path.GetFileName(path) ?? "New tab", path), point);
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

    // External drops arrive in this controller's coordinate space already.
    internal void HandleExternalFiles(IReadOnlyList<string> paths, WorkspaceDragEvent input)
    {
        if (input.Kind != WorkspaceDragEventKind.Started)
        {
            Continue(input.Kind, input.Position);
            return;
        }

        if (paths.Count == 0)
        {
            return;
        }

        string label = paths.Count == 1
            ? Path.GetFileName(paths[0])
            : $"{paths.Count} files";
        Start(new ExternalFilesDragPayload(paths, label), input.Position);
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
        _overlay.Show(payload.Label, payload.ImagePath);
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

        // Dropping files on nothing still opens them.
        if (payload is ExternalFilesDragPayload files)
        {
            OpenExternalFiles(files.Paths, target);
            return;
        }

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

    private void OpenExternalFiles(IReadOnlyList<string> paths, WorkspaceDropTarget? target)
    {
        if (target is null)
        {
            _commands.OpenImage(paths[0]);
        }
        else
        {
            _commands.OpenImageInNewTab(paths[0], target);
        }

        // The first file selected the target pane, so the rest land in it.
        for (int index = 1; index < paths.Count; index++)
        {
            _commands.OpenImageInNewTab(paths[index]);
        }
    }

    private void Update(PointF point)
    {
        _target = null;
        RectangleF insertionMarker = RectangleF.Empty;
        RectangleF landingBounds = RectangleF.Empty;

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
                break;
            }

            RectangleF contentBounds = paneView.ContentBounds;
            contentBounds.Offset(paneBounds.Location);
            if (!contentBounds.Contains(point))
            {
                break;
            }

            ViewerPane pane = paneView.Pane;
            bool fromThisPane = _payload is TabDragPayload tab && ReferenceEquals(tab.SourcePane, pane);
            // An empty pane has nothing to split away from, so all of it takes the drop.
            WorkspacePaneDropSide? side = pane.IsEmpty ? null : GetSplitSide(contentBounds, point);
            if (side is null)
            {
                // Dropping a tab into the pane it came from would change nothing.
                if (!fromThisPane)
                {
                    _target = new WorkspaceTabDropTarget(pane, pane.Count);
                    landingBounds = contentBounds;
                }
            }
            else if (!fromThisPane || pane.Count > 1)
            {
                _target = new WorkspacePaneDropTarget(pane, side.Value);
                landingBounds = GetLandingBounds(contentBounds, side.Value);
            }

            break;
        }

        _overlay.Update(point, insertionMarker, landingBounds);
    }

    /// <summary>Picks the pane edge the point is nearest to, relative to the pane's size.</summary>
    /// <returns><see langword="null"/> in the middle of the pane, which does not split.</returns>
    internal static WorkspacePaneDropSide? GetSplitSide(RectangleF bounds, PointF point)
    {
        float x = (point.X - bounds.Left) / bounds.Width;
        float y = (point.Y - bounds.Top) / bounds.Height;
        float horizontalDistance = MathF.Min(x, 1.0f - x);
        float verticalDistance = MathF.Min(y, 1.0f - y);
        if (MathF.Min(horizontalDistance, verticalDistance) >= SplitEdgeFraction)
        {
            return null;
        }

        return horizontalDistance < verticalDistance
            ? (x < 0.5f ? WorkspacePaneDropSide.Left : WorkspacePaneDropSide.Right)
            : (y < 0.5f ? WorkspacePaneDropSide.Top : WorkspacePaneDropSide.Bottom);
    }

    /// <summary>The half of the pane that a split on this side would give to the dropped item.</summary>
    internal static RectangleF GetLandingBounds(RectangleF bounds, WorkspacePaneDropSide side)
    {
        float halfWidth = bounds.Width / 2.0f;
        float halfHeight = bounds.Height / 2.0f;
        return side switch
        {
            WorkspacePaneDropSide.Left => bounds with { Width = halfWidth },
            WorkspacePaneDropSide.Top => bounds with { Height = halfHeight },
            WorkspacePaneDropSide.Right => bounds with { X = bounds.X + halfWidth, Width = halfWidth },
            WorkspacePaneDropSide.Bottom => bounds with { Y = bounds.Y + halfHeight, Height = halfHeight },
            _ => throw new ArgumentOutOfRangeException(nameof(side)),
        };
    }

    // The image path is what the drag shows a thumbnail of.
    private abstract record WorkspaceDragPayload(string Label, string? ImagePath);

    private sealed record TabDragPayload(ViewerPane SourcePane, ViewerTab Tab, string TabLabel, string? TabPath)
        : WorkspaceDragPayload(TabLabel, TabPath);

    private sealed record ImageDragPayload(string Path, string ImageLabel)
        : WorkspaceDragPayload(ImageLabel, Path);

    private sealed record ExternalFilesDragPayload(IReadOnlyList<string> Paths, string FilesLabel)
        : WorkspaceDragPayload(FilesLabel, Paths[0]);
}
