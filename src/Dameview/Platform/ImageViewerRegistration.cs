using Microsoft.Win32;
using Windows.Win32.UI.Shell;
using static Windows.Win32.PInvoke;

namespace Dameview.Platform;

internal static class ImageViewerRegistration
{
    private const string ProgId = "Dameview.Image";
    private const string ClassesRegistryPath = @"Software\Classes";
    private const string CapabilitiesRegistryPath = @"Software\Dameview\Capabilities";
    private const string RegisteredApplicationsRegistryPath = @"Software\RegisteredApplications";

    internal static void Register(string executablePath, IEnumerable<string> supportedExtensions)
    {
        Unregister(notifyShell: false);

        using (RegistryKey progId = Registry.CurrentUser.CreateSubKey($@"{ClassesRegistryPath}\{ProgId}"))
        {
            progId.SetValue(null, "Image");
            using RegistryKey icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue(null, $"\"{executablePath}\",0");
            using RegistryKey command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue(null, $"\"{executablePath}\" \"%1\"");
        }

        using RegistryKey capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesRegistryPath);
        capabilities.SetValue("ApplicationName", "Dameview");
        capabilities.SetValue("ApplicationDescription", "View images with Dameview");
        using RegistryKey fileAssociations = capabilities.CreateSubKey("FileAssociations");

        foreach (string extension in supportedExtensions
                     .Where(IsValidFileExtension)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase))
        {
            fileAssociations.SetValue(extension, ProgId);
            using RegistryKey openWith = Registry.CurrentUser.CreateSubKey(
                $@"{ClassesRegistryPath}\{extension}\OpenWithProgids");
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        using RegistryKey registeredApplications = Registry.CurrentUser.CreateSubKey(RegisteredApplicationsRegistryPath);
        registeredApplications.SetValue("Dameview", CapabilitiesRegistryPath);
        NotifyAssociationChanged();
    }

    internal static void Unregister()
    {
        Unregister(notifyShell: true);
    }

    private static void Unregister(bool notifyShell)
    {
        using (RegistryKey? fileAssociations = Registry.CurrentUser.OpenSubKey(
                   $@"{CapabilitiesRegistryPath}\FileAssociations"))
        {
            if (fileAssociations is not null)
            {
                foreach (string extension in fileAssociations.GetValueNames().Where(IsValidFileExtension))
                {
                    using RegistryKey? openWith = Registry.CurrentUser.OpenSubKey(
                        $@"{ClassesRegistryPath}\{extension}\OpenWithProgids",
                        writable: true);
                    openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
                }
            }
        }

        using (RegistryKey? registeredApplications = Registry.CurrentUser.OpenSubKey(
                   RegisteredApplicationsRegistryPath,
                   writable: true))
        {
            registeredApplications?.DeleteValue("Dameview", throwOnMissingValue: false);
        }

        Registry.CurrentUser.DeleteSubKeyTree(CapabilitiesRegistryPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRegistryPath}\{ProgId}", throwOnMissingSubKey: false);

        if (notifyShell)
        {
            NotifyAssociationChanged();
        }
    }

    private static bool IsValidFileExtension(string extension)
    {
        return extension.Length > 1
            && extension[0] == '.'
            && extension.IndexOfAny(['\\', '/', '\0']) < 0;
    }

    private static unsafe void NotifyAssociationChanged()
    {
        SHChangeNotify(SHCNE_ID.SHCNE_ASSOCCHANGED, SHCNF_FLAGS.SHCNF_IDLIST, null, null);
    }
}
