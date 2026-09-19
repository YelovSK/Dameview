using System.Drawing;

namespace Dameview.Win32.Input;

internal readonly record struct WindowFileDragEvent(
    WindowFileDragKind Kind,
    PointF Position,
    IReadOnlyList<string> Paths);

internal enum WindowFileDragKind
{
    Entered,
    Moved,
    Dropped,
    Left,
}
