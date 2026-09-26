using System.Diagnostics;
using Dameview.Diagnostics;
using Dameview.Win32;

namespace Dameview.Installation;

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
    private const string OldExecutablePattern = "Dameview.old.*.exe";
    private static readonly InstalledProgramRegistration InstalledProgram = new("Dameview");
    private static readonly FileAssociationRegistration FileAssociations = new("Dameview", "Dameview.Image");

    internal static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "Dameview");

    internal static string InstalledExecutablePath => Path.Combine(InstallDirectory, "Dameview.exe");

    private static string StagedExecutablePath => Path.Combine(InstallDirectory, "Dameview.new.exe");

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
        Log.Info("Installation", "Staging executable.");
        File.Copy(sourcePath, StagedExecutablePath, overwrite: true);
        Log.Info("Installation", "Replacing installed executable.");
        ReplaceExecutable(StagedExecutablePath, InstalledExecutablePath);
        Log.Info("Installation", "Installed executable replaced, updating registration.");
        ShellIntegration.CreateShortcut(
            GetStartMenuShortcutPath(), InstalledExecutablePath, InstallDirectory,
            "Dameview image viewer", InstalledExecutablePath);
        InstalledProgram.Register(
            "Dameview", GetDisplayVersion(InstalledExecutablePath), InstallDirectory,
            InstalledExecutablePath, UninstallArgument);
        FileAssociations.Register(
            applicationName: "Dameview",
            applicationDescription: "View images with Dameview",
            fileTypeDescription: "Image",
            executablePath: InstalledExecutablePath,
            supportedExtensions: supportedExtensions);
        CleanUpOldExecutables();
    }

    internal static void ReplaceExecutable(string stagedPath, string installedPath)
    {
        if (!File.Exists(installedPath))
        {
            File.Move(stagedPath, installedPath);
            return;
        }

        string oldPath = Path.Combine(
            Path.GetDirectoryName(installedPath)!,
            $"Dameview.old.{Guid.NewGuid():N}.exe");

        File.Move(installedPath, oldPath);
        Log.Info("Installation", "Previous executable renamed aside.");

        try
        {
            File.Move(stagedPath, installedPath);
        }
        catch (Exception placementException)
        {
            try
            {
                File.Move(oldPath, installedPath);
            }
            catch (Exception rollbackException)
            {
                Log.Error("Installation", "Could not restore the previous executable.", rollbackException);
                throw new AggregateException("Could not install or restore the executable.", placementException, rollbackException);
            }

            Log.Warning("Installation", "New executable could not be placed, previous executable restored.");
            throw;
        }
    }

    internal static void CleanUpOldExecutables()
    {
        foreach (string path in GetOldExecutablePaths())
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Warning("Installation", $"Old executable could not be removed yet ({exception.GetType().Name}, 0x{exception.HResult:X8}).");
            }
        }
    }

    private static string[] GetOldExecutablePaths()
    {
        if (!Directory.Exists(InstallDirectory))
        {
            return [];
        }

        try
        {
            return Directory.GetFiles(InstallDirectory, OldExecutablePattern);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning("Installation", $"Could not inspect old executables ({exception.GetType().Name}, 0x{exception.HResult:X8}).");
            return [];
        }
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

        FileAssociations.Unregister();
        InstalledProgram.Unregister();
    }

    internal static void DeleteInstalledFilesAfterExit()
    {
        if (!Directory.Exists(InstallDirectory))
        {
            return;
        }

        string[] oldPaths = GetOldExecutablePaths();
        DeleteAfterExit([InstalledExecutablePath, StagedExecutablePath, .. oldPaths], InstallDirectory);
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
            Arguments = $"/D /C \"{command.Replace("\"", "\"\"")}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        Process.Start(startInfo);
    }

    private static bool IsInstalledExecutable()
    {
        return string.Equals(
            Path.GetFullPath(Environment.ProcessPath ?? string.Empty),
            Path.GetFullPath(InstalledExecutablePath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetStartMenuShortcutPath()
    {
        return Path.Combine(NativeMethods.GetProgramsPath(), "Dameview.lnk");
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

    internal static string GetDisplayVersion(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return "unknown";
        }

        var version = FileVersionInfo.GetVersionInfo(path);
        return version.ProductVersion?.Split('+')[0] ?? version.FileVersion ?? "unknown";
    }
}
