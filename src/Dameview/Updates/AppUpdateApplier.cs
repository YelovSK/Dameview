using System.Diagnostics;
using System.Globalization;
using Dameview.Imaging;
using Dameview.Platform;

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
        WaitForExit(previousProcessId);
        using var imageDecoder = new ImageDecoder();
        AppInstallation.Install(updatePath, imageDecoder.GetProbablySupportedExtensions());
        AppInstallation.LaunchInstalled();
        AppInstallation.DeleteCurrentExecutableAfterExit(updatePath);
        return true;
    }

    internal static void Launch(string updatePath)
    {
        string currentPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the executable path.");
        string updateDirectory = Path.GetDirectoryName(updatePath)
            ?? throw new InvalidOperationException("The update path has no containing directory.");
        string helperPath = Path.Combine(updateDirectory, "Dameview.updater.exe");
        File.Copy(currentPath, helperPath, overwrite: true);

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
            _ = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the temporary updater.");
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
