using Microsoft.Win32;

namespace Dameview.Win32;

internal sealed class FileAssociationRegistration(string applicationId, string progId)
{
    private const string ClassesRegistryPath = @"Software\Classes";
    private const string RegisteredApplicationsRegistryPath = @"Software\RegisteredApplications";
    private readonly string _capabilitiesRegistryPath = $@"Software\{applicationId}\Capabilities";

    internal void Register(
        string applicationName, string applicationDescription, string fileTypeDescription,
        string executablePath, IEnumerable<string> supportedExtensions)
    {
        Unregister(notifyShell: false);

        using (RegistryKey progIdKey = Registry.CurrentUser.CreateSubKey($@"{ClassesRegistryPath}\{progId}"))
        {
            progIdKey.SetValue(null, fileTypeDescription);
            using RegistryKey icon = progIdKey.CreateSubKey("DefaultIcon");
            icon.SetValue(null, $"\"{executablePath}\",0");
            using RegistryKey command = progIdKey.CreateSubKey(@"shell\open\command");
            command.SetValue(null, $"\"{executablePath}\" \"%1\"");
        }

        using RegistryKey capabilities = Registry.CurrentUser.CreateSubKey(_capabilitiesRegistryPath);
        capabilities.SetValue("ApplicationName", applicationName);
        capabilities.SetValue("ApplicationDescription", applicationDescription);
        using RegistryKey fileAssociations = capabilities.CreateSubKey("FileAssociations");

        foreach (string extension in supportedExtensions
                     .Where(IsValidFileExtension)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase))
        {
            fileAssociations.SetValue(extension, progId);
            using RegistryKey openWith = Registry.CurrentUser.CreateSubKey(
                $@"{ClassesRegistryPath}\{extension}\OpenWithProgids");
            openWith.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        using RegistryKey registeredApplications = Registry.CurrentUser.CreateSubKey(RegisteredApplicationsRegistryPath);
        registeredApplications.SetValue(applicationId, _capabilitiesRegistryPath);
        ShellIntegration.NotifyAssociationChanged();
    }

    internal void Unregister()
    {
        Unregister(notifyShell: true);
    }

    private void Unregister(bool notifyShell)
    {
        using (RegistryKey? fileAssociations = Registry.CurrentUser.OpenSubKey(
                   $@"{_capabilitiesRegistryPath}\FileAssociations"))
        {
            if (fileAssociations is not null)
            {
                foreach (string extension in fileAssociations.GetValueNames().Where(IsValidFileExtension))
                {
                    using RegistryKey? openWith = Registry.CurrentUser.OpenSubKey(
                        $@"{ClassesRegistryPath}\{extension}\OpenWithProgids",
                        writable: true);
                    openWith?.DeleteValue(progId, throwOnMissingValue: false);
                }
            }
        }

        using (RegistryKey? registeredApplications = Registry.CurrentUser.OpenSubKey(
                   RegisteredApplicationsRegistryPath,
                   writable: true))
        {
            registeredApplications?.DeleteValue(applicationId, throwOnMissingValue: false);
        }

        Registry.CurrentUser.DeleteSubKeyTree(_capabilitiesRegistryPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRegistryPath}\{progId}", throwOnMissingSubKey: false);

        if (notifyShell)
        {
            ShellIntegration.NotifyAssociationChanged();
        }
    }

    private static bool IsValidFileExtension(string extension)
    {
        return extension.Length > 1
            && extension[0] == '.'
            && extension.IndexOfAny(['\\', '/', '\0']) < 0;
    }
}
