using System.Drawing;
using System.Runtime.InteropServices.Marshalling;
using Dameview.Win32.Input;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.SystemServices;
using static Windows.Win32.PInvoke;

namespace Dameview.Win32;

// Reports dragged files while they are still over the window; WM_DROPFILES only fires on drop.
[GeneratedComClass]
internal sealed partial class FileDropTarget : IDropTarget
{
    private readonly nint _window;
    private readonly Action<WindowFileDragEvent> _dispatch;
    private string[] _paths = [];

    internal FileDropTarget(nint window, Action<WindowFileDragEvent> dispatch)
    {
        _window = window;
        _dispatch = dispatch;
    }

    public unsafe void DragEnter(
        IDataObject pDataObj,
        MODIFIERKEYS_FLAGS grfKeyState,
        POINTL pt,
        DROPEFFECT* pdwEffect)
    {
        _paths = GetFilePaths(pDataObj);
        *pdwEffect = GetEffect(_paths);
        Dispatch(WindowFileDragKind.Entered, pt);
    }

    public unsafe void DragOver(
        MODIFIERKEYS_FLAGS grfKeyState,
        POINTL pt,
        DROPEFFECT* pdwEffect)
    {
        *pdwEffect = GetEffect(_paths);
        Dispatch(WindowFileDragKind.Moved, pt);
    }

    public void DragLeave()
    {
        if (_paths.Length == 0)
        {
            return;
        }

        _paths = [];
        _dispatch(new WindowFileDragEvent(WindowFileDragKind.Left, PointF.Empty, []));
    }

    public unsafe void Drop(
        IDataObject pDataObj,
        MODIFIERKEYS_FLAGS grfKeyState,
        POINTL pt,
        DROPEFFECT* pdwEffect)
    {
        // The drag can start over another target, so DragEnter may never have run here.
        if (_paths.Length == 0)
        {
            _paths = GetFilePaths(pDataObj);
        }

        *pdwEffect = GetEffect(_paths);
        Dispatch(WindowFileDragKind.Dropped, pt);
        _paths = [];
    }

    private static DROPEFFECT GetEffect(string[] paths) =>
        paths.Length > 0 ? DROPEFFECT.DROPEFFECT_COPY : DROPEFFECT.DROPEFFECT_NONE;

    private static unsafe string[] GetFilePaths(IDataObject data)
    {
        FORMATETC format = new()
        {
            cfFormat = (ushort)CLIPBOARD_FORMAT.CF_HDROP,
            ptd = null,
            dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = (uint)TYMED.TYMED_HGLOBAL,
        };

        // Dragged text and the like have no CF_HDROP. QueryGetData reports that without throwing.
        if ((int)data.QueryGetData(&format) != 0)
        {
            return [];
        }

        data.GetData(&format, out STGMEDIUM medium);
        try
        {
            return NativeMethods.GetDroppedFilePaths((nint)medium.u.hGlobal.Value);
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private void Dispatch(WindowFileDragKind kind, POINTL point)
    {
        if (_paths.Length == 0)
        {
            return;
        }

        // OLE reports screen coordinates; the window works in client pixels.
        var client = new Point(point.x, point.y);
        _ = ScreenToClient((HWND)_window, ref client);
        _dispatch(new WindowFileDragEvent(kind, new PointF(client.X, client.Y), _paths));
    }
}
