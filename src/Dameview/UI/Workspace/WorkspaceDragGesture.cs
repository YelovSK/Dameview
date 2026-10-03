using System.Drawing;

namespace Dameview.UI.Workspace;

/// <summary>
/// Turns a held press into a workspace drag once the pointer has moved far enough that it
/// can't be a slightly shaky click.
/// </summary>
internal sealed class WorkspaceDragGesture(Action<WorkspaceDragEvent> raise)
{
    private const float ThresholdDips = 4.0f;

    private PointF _pressPosition;
    private bool _dragging;

    internal void Press(PointF position)
    {
        _pressPosition = position;
        _dragging = false;
    }

    /// <summary>Call only while pressed.</summary>
    /// <returns>Whether the press has become a drag.</returns>
    internal bool Move(PointF position)
    {
        if (!_dragging
            && (MathF.Abs(position.X - _pressPosition.X) >= ThresholdDips
                || MathF.Abs(position.Y - _pressPosition.Y) >= ThresholdDips))
        {
            _dragging = true;
            raise(new WorkspaceDragEvent(WorkspaceDragEventKind.Started, position));
        }

        if (_dragging)
        {
            raise(new WorkspaceDragEvent(WorkspaceDragEventKind.Moved, position));
        }

        return _dragging;
    }

    /// <returns>Whether the press had become a drag.</returns>
    internal bool Release(PointF position) => End(WorkspaceDragEventKind.Completed, position);

    /// <returns>Whether the press had become a drag.</returns>
    internal bool Cancel(PointF position) => End(WorkspaceDragEventKind.Cancelled, position);

    private bool End(WorkspaceDragEventKind kind, PointF position)
    {
        bool wasDragging = _dragging;
        _dragging = false;
        if (wasDragging)
        {
            raise(new WorkspaceDragEvent(kind, position));
        }

        return wasDragging;
    }
}
