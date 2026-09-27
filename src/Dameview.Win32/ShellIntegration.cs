using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using static Windows.Win32.PInvoke;

namespace Dameview.Win32;

internal static class ShellIntegration
{
    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-C000-000000000046");

    internal static unsafe void CreateShortcut(
        string shortcutPath, string targetPath, string workingDirectory,
        string description, string iconPath)
    {
        HRESULT result = CoCreateInstance(ShellLinkClassId, null, CLSCTX.CLSCTX_INPROC_SERVER, out IShellLinkW shellLink);
        result.ThrowOnFailure();
        shellLink.SetPath(targetPath);
        shellLink.SetWorkingDirectory(workingDirectory);
        shellLink.SetDescription(description);
        shellLink.SetIconLocation(iconPath, 0);
        ((IPersistFile)shellLink).Save(shortcutPath, true);
    }

    // Unlike starting explorer.exe, this reaches a replacement file manager set as the default.
    // The shell call waits on that file manager, which may never answer (File Pilot doesn't),
    // so it gets its own thread that cannot hold up the UI or process exit.
    internal static void ShowInFolder(string path)
    {
        var thread = new Thread(() => ShowInFolderBlocking(path))
        {
            IsBackground = true,
            Name = "Dameview show in folder",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static unsafe void ShowInFolderBlocking(string path)
    {
        ITEMIDLIST* item = ILCreateFromPath(path);
        if (item is null)
        {
            return;
        }

        try
        {
            _ = SHOpenFolderAndSelectItems(item, 0, null, 0);
        }
        finally
        {
            ILFree(item);
        }
    }

    // Blocks until an app is picked or the dialog is dismissed, which is not a failure.
    internal static unsafe bool TryShowOpenWith(nint owner, string path)
    {
        fixed (char* file = path)
        {
            var info = new OPENASINFO { pcszFile = file, oaifInFlags = OPEN_AS_INFO_FLAGS.OAIF_EXEC };
            HRESULT result = SHOpenWithDialog((HWND)owner, &info);
            return result.Succeeded || result == HRESULT_FROM_WIN32(WIN32_ERROR.ERROR_CANCELLED);
        }
    }

    internal static bool TryShowProperties(nint owner, string path) =>
        SHObjectProperties((HWND)owner, SHOP_TYPE.SHOP_FILEPATH, path, null);

    // The shell shows its own errors, and still asks first when the file cannot be recycled.
    internal static unsafe bool TryMoveToRecycleBin(nint owner, string path)
    {
        // The shell expects a list terminated by an extra null.
        fixed (char* from = path + '\0')
        {
            var operation = new SHFILEOPSTRUCTW
            {
                hwnd = (HWND)owner,
                wFunc = FO_DELETE,
                pFrom = from,
                fFlags = (ushort)(FILEOPERATION_FLAGS.FOF_ALLOWUNDO
                    | FILEOPERATION_FLAGS.FOF_NOCONFIRMATION
                    | FILEOPERATION_FLAGS.FOF_SILENT
                    | FILEOPERATION_FLAGS.FOF_WANTNUKEWARNING),
            };
            return SHFileOperation(ref operation) == 0 && !operation.fAnyOperationsAborted;
        }
    }

    internal static unsafe void NotifyAssociationChanged()
    {
        SHChangeNotify(SHCNE_ID.SHCNE_ASSOCCHANGED, SHCNF_FLAGS.SHCNF_IDLIST, null, null);
    }
}
