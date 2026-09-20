using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using static Windows.Win32.PInvoke;

namespace Dameview.Win32;

/// <summary>The system file picker, limited to the extensions the caller can read.</summary>
internal static partial class FilePicker
{
    // The dialog reports a dismissed pick as a failed call rather than an empty result.
    private static readonly int Cancelled = HRESULT_FROM_WIN32(WIN32_ERROR.ERROR_CANCELLED);

    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <returns>The chosen file, or <see langword="null"/> when the dialog was dismissed.</returns>
    internal static unsafe string? PickFile(nint owner, string filterName, IEnumerable<string> extensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterName);
        string pattern = string.Join(';', extensions.Select(extension => '*' + extension));
        Guid clsid = typeof(FileOpenDialog).GUID;
        Guid iid = typeof(IFileOpenDialog).GUID;
        void* instance;
        Marshal.ThrowExceptionForHR(
            CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, &instance));

        object wrapper = Wrappers.GetOrCreateObjectForComInstance(
            (nint)instance,
            CreateObjectFlags.UniqueInstance);
        try
        {
            var dialog = (IFileOpenDialog)wrapper;
            fixed (char* name = filterName)
            fixed (char* spec = pattern)
            {
                var filter = new COMDLG_FILTERSPEC { pszName = name, pszSpec = spec };
                dialog.SetFileTypes(1, &filter);
            }

            dialog.SetOptions(
                FILEOPENDIALOGOPTIONS.FOS_FILEMUSTEXIST
                | FILEOPENDIALOGOPTIONS.FOS_PATHMUSTEXIST
                | FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM);
            dialog.Show((HWND)owner);
            dialog.GetResult(out IShellItem item);
            return GetPath(item);
        }
        catch (COMException exception) when (exception.HResult == Cancelled)
        {
            return null;
        }
        finally
        {
            (wrapper as IDisposable)?.Dispose();
            Marshal.Release((nint)instance);
        }
    }

    private static unsafe string? GetPath(IShellItem item)
    {
        PWSTR path = default;
        try
        {
            item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, &path);
            return path.Value is null ? null : path.ToString();
        }
        finally
        {
            CoTaskMemFree(path.Value);
        }
    }

    [LibraryImport("ole32")]
    private static unsafe partial int CoCreateInstance(
        Guid* rclsid,
        void* pUnkOuter,
        CLSCTX dwClsContext,
        Guid* riid,
        void** ppv);
}
