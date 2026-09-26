using System.Diagnostics;
using System.Globalization;
using Dameview.Diagnostics;
using Dameview.Imaging.Decoding;
using Dameview.Installation;

namespace Dameview.Updates;

internal static class AppUpdateApplier
{
    private const string ApplyArgument = "--apply-update";

    internal static bool TryApply(string[] args)
    {
        if (args.Length != 3
            || !args[0].Equals(ApplyArgument, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(args[1], out int previousProcessId)
            || !File.Exists(args[2]))
        {
            return false;
        }

        string updatePath = Path.GetFullPath(args[2]);
        Log.Info("Updates", $"Updater started; waiting for viewer process {previousProcessId} to exit.");
        WaitForExit(previousProcessId);
        Log.Info("Updates", "Previous viewer exited; installing update.");
        try
        {
            using var imageDecoder = new ImageDecoder();
            AppInstallation.Install(updatePath, imageDecoder.GetProbablySupportedExtensions());
            Log.Info("Updates", "Update installed; launching viewer.");
        }
        catch (Exception exception)
        {
            Log.Error("Updates", "Could not install the update; reopening the installed app.", exception);
            if (!File.Exists(AppInstallation.InstalledExecutablePath))
            {
                throw;
            }
        }

        AppInstallation.LaunchInstalled();
        Log.Info("Updates", "Installed viewer launched.");
        AppInstallation.DeleteCurrentExecutableAfterExit(updatePath);
        return true;
    }

    internal static void Launch(string updatePath)
    {
        string currentPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the executable path.");
        string updateDirectory = Path.GetDirectoryName(updatePath)
            ?? throw new InvalidOperationException("The update path has no containing directory.");
        string helperPath = Path.Combine(updateDirectory, $"Dameview.updater.{Guid.NewGuid():N}.exe");
        Log.Info("Updates", "Preparing updater handoff.");
        File.Copy(currentPath, helperPath);

        var startInfo = new ProcessStartInfo(helperPath)
        {
            UseShellExecute = false,
            WorkingDirectory = updateDirectory,
        };
        startInfo.ArgumentList.Add(ApplyArgument);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(Path.GetFullPath(updatePath));
        try
        {
            using var helper = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the temporary updater.");
            Log.Info("Updates", $"Updater process {helper.Id} started; closing viewer.");
        }
        catch
        {
            File.Delete(helperPath);
            throw;
        }
    }

    private static void WaitForExit(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit();
        }
        catch (ArgumentException)
        {
            // The viewer exited before the updater opened the process.
        }
    }
}
