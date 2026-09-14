using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using static Windows.Win32.PInvoke;

namespace Dameview.Platform;

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

    internal static unsafe void NotifyAssociationChanged()
    {
        SHChangeNotify(SHCNE_ID.SHCNE_ASSOCCHANGED, SHCNF_FLAGS.SHCNF_IDLIST, null, null);
    }
}
