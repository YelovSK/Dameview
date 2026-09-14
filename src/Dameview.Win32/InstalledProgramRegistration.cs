using Microsoft.Win32;

namespace Dameview.Win32;

// Per-user installed-program metadata for an application with an uninstall command.
internal sealed class InstalledProgramRegistration(string applicationId)
{
    private readonly string _registryPath =
        $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{applicationId}";

    internal void Register(
        string displayName, string displayVersion, string installDirectory,
        string executablePath, string uninstallArguments)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(_registryPath);
        key.SetValue("DisplayName", displayName);
        key.SetValue("DisplayVersion", displayVersion);
        key.SetValue("InstallLocation", installDirectory);
        key.SetValue("DisplayIcon", executablePath);
        key.SetValue("UninstallString", $"\"{executablePath}\" {uninstallArguments}");
        key.SetValue("EstimatedSize", checked((int)((new FileInfo(executablePath).Length + 1023) / 1024)), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    internal void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_registryPath, throwOnMissingSubKey: false);
    }
}
