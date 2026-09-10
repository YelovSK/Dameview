using System.Drawing;

namespace Dameview.UI;

internal enum WorkspaceDragEventKind
{
    Started,
    Moved,
    Completed,
    Cancelled,
}

internal readonly record struct WorkspaceDragEvent(
    WorkspaceDragEventKind Kind,
    PointF Position);
