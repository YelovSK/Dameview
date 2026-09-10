using System.Diagnostics;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;
using static Windows.Win32.PInvoke;

namespace Dameview.Platform;

internal enum AppInstallationAction
{
    Install,
    Update,
    Reinstall,
    Uninstall,
}

internal readonly record struct AppInstallationRequest(
    AppInstallationAction Action,
    string CurrentVersion,
    string? InstalledVersion);

internal static class AppInstallation
{
    private const string InstallArgument = "--install";
    private const string UninstallArgument = "--uninstall";
    private const string UninstallRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Dameview";
    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-C000-000000000046");

    internal static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "Dameview");

    internal static string InstalledExecutablePath => Path.Combine(InstallDirectory, "Dameview.exe");

    internal static Version? GetInstalledRunningVersion()
    {
        return IsInstalledExecutable() ? ReadVersion(Environment.ProcessPath) : null;
    }

    internal static AppInstallationRequest? GetRequest(string[] args)
    {
        if (args.Length == 1 && args[0].Equals(UninstallArgument, StringComparison.OrdinalIgnoreCase))
        {
            return new AppInstallationRequest(
                AppInstallationAction.Uninstall,
                GetDisplayVersion(Environment.ProcessPath),
                File.Exists(InstalledExecutablePath) ? GetDisplayVersion(InstalledExecutablePath) : null);
        }

        bool explicitlyRequested = args.Length == 1
            && args[0].Equals(InstallArgument, StringComparison.OrdinalIgnoreCase);
        if (!explicitlyRequested && (IsInstalledExecutable() || args.Length != 0))
        {
            return null;
        }

        string? sourcePath = Environment.ProcessPath;
        Version? currentVersion = ReadVersion(sourcePath);
        if (!File.Exists(InstalledExecutablePath))
        {
            return new AppInstallationRequest(
                AppInstallationAction.Install,
                GetDisplayVersion(sourcePath),
                null);
        }

        Version? installedVersion = ReadVersion(InstalledExecutablePath);
        AppInstallationAction action = currentVersion > installedVersion
            ? AppInstallationAction.Update
            : AppInstallationAction.Reinstall;
        return new AppInstallationRequest(
            action,
            GetDisplayVersion(sourcePath),
            GetDisplayVersion(InstalledExecutablePath));
    }

    internal static void Install(string sourcePath, IEnumerable<string> supportedExtensions)
    {
        Directory.CreateDirectory(InstallDirectory);
        string stagedPath = Path.Combine(InstallDirectory, "Dameview.new.exe");
        File.Copy(sourcePath, stagedPath, overwrite: true);
        File.Move(stagedPath, InstalledExecutablePath, overwrite: true);
        CreateStartMenuShortcut();
        WriteUninstallRegistration();
        ImageViewerRegistration.Register(InstalledExecutablePath, supportedExtensions);
    }

    internal static void LaunchInstalled()
    {
        Process.Start(new ProcessStartInfo(InstalledExecutablePath)
        {
            UseShellExecute = true,
            WorkingDirectory = InstallDirectory,
        });
    }

    internal static void Uninstall()
    {
        string shortcutPath = GetStartMenuShortcutPath();
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }

        ImageViewerRegistration.Unregister();
        Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistryPath, throwOnMissingSubKey: false);
    }

    internal static void DeleteInstalledFilesAfterExit()
    {
        if (!File.Exists(InstalledExecutablePath))
        {
            return;
        }

        if (!IsInstalledExecutable())
        {
            File.Delete(InstalledExecutablePath);
            Directory.Delete(InstallDirectory, recursive: false);
            return;
        }

        DeleteAfterExit([InstalledExecutablePath], InstallDirectory);
    }

    internal static void DeleteCurrentExecutableAfterExit(string additionalPath)
    {
        string path = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the executable path.");
        DeleteAfterExit([path, additionalPath], directory: null);
    }

    private static void DeleteAfterExit(IReadOnlyList<string> paths, string? directory)
    {
        string command = "choice /C Y /N /D Y /T 1 > nul";
        foreach (string path in paths)
        {
            command += $" & del /F /Q \"{path}\"";
        }

        if (directory is not null)
        {
            command += $" & rmdir \"{directory}\"";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add("/D");
        startInfo.ArgumentList.Add("/C");
        startInfo.ArgumentList.Add(command);
        Process.Start(startInfo);
    }

    private static bool IsInstalledExecutable()
    {
        return string.Equals(
            Path.GetFullPath(Environment.ProcessPath ?? string.Empty),
            Path.GetFullPath(InstalledExecutablePath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void CreateStartMenuShortcut()
    {
        HRESULT result = CoCreateInstance(ShellLinkClassId, null, CLSCTX.CLSCTX_INPROC_SERVER, out IShellLinkW shellLink);
        result.ThrowOnFailure();
        shellLink.SetPath(InstalledExecutablePath);
        shellLink.SetWorkingDirectory(InstallDirectory);
        shellLink.SetDescription("Dameview image viewer");
        shellLink.SetIconLocation(InstalledExecutablePath, 0);
        ((IPersistFile)shellLink).Save(GetStartMenuShortcutPath(), true);
    }

    private static string GetStartMenuShortcutPath()
    {
        return Path.Combine(NativeMethods.GetProgramsPath(), "Dameview.lnk");
    }

    private static void WriteUninstallRegistration()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallRegistryPath);
        string displayVersion = GetDisplayVersion(InstalledExecutablePath);
        key.SetValue("DisplayName", "Dameview");
        key.SetValue("DisplayVersion", displayVersion);
        key.SetValue("InstallLocation", InstallDirectory);
        key.SetValue("DisplayIcon", InstalledExecutablePath);
        key.SetValue("UninstallString", $"\"{InstalledExecutablePath}\" {UninstallArgument}");
        key.SetValue("EstimatedSize", checked((int)((new FileInfo(InstalledExecutablePath).Length + 1023) / 1024)), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    internal static Version? ReadVersion(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        return Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion, out Version? version)
            ? version
            : null;
    }

    private static string GetDisplayVersion(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return "unknown";
        }

        var version = FileVersionInfo.GetVersionInfo(path);
        return version.ProductVersion?.Split('+')[0] ?? version.FileVersion ?? "unknown";
    }
}
