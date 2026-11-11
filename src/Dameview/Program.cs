using System.Runtime.CompilerServices;
using Dameview.App;
using Dameview.Diagnostics;
using Dameview.Installation;
using Dameview.Settings;
using Dameview.Updates;
using Dameview.Win32;

namespace Dameview;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        StartupTrace.Begin();
        StartupTrace.Mark("runtime");
        InitializeLogger();
        StartupTrace.Mark("log");
        NativeMethods.EnablePerMonitorDpiAwareness();
        NativeMethods.InitializeComApartment(ComApartment.ApartmentThreaded);
        StartupTrace.Mark("com");

        try
        {
            if (AppUpdateApplier.TryApply(args))
            {
                return 0;
            }

            if (AppInstallation.GetSilentAction(args) is { } silentAction)
            {
                return AppInstallation.RunSilent(silentAction);
            }

            if (AppInstallation.GetRequest(args) is { } installationRequest)
            {
                bool runPortable;
                using (var installer = new InstallerApp(installationRequest))
                {
                    runPortable = installer.Run();
                }

                if (!runPortable)
                {
                    return 0;
                }

                args = [];
            }

            StartupTrace.Mark("mode");
            AppSettings startupSettings = SettingsService.LoadForStartup();
            StartupTrace.Mark("settings");
            SingleInstanceHost? instance = null;
            if (startupSettings.SingleInstance)
            {
                switch (SingleInstanceHost.AcquireOrForward(args))
                {
                    case SingleInstanceHost acquired:
                        instance = acquired;
                        break;

                    case ForwardedToRunningCopy:
                        return 0;

                    case RunningCopyUnreachable:
                        Log.Warning("App", "The running copy did not answer, so this one opens its own window.");
                        break;
                }
            }

            using (instance)
            {
                StartupTrace.Mark("instance");
                using var app = new DameviewApp(startupSettings);
                app.Run(args);
            }

            return 0;
        }
        catch (Exception exception)
        {
            Log.Error("App", "Unhandled application exception.", exception);
            throw;
        }
        finally
        {
            Log.Shutdown();
            NativeMethods.UninitializeComApartment();
        }
    }

    private static void InitializeLogger()
    {
        Log.Initialize();
        Log.Info("App", $"Dameview {AppInstallation.CurrentDisplayVersion}");
        Version osVersion = Environment.OSVersion.Version;
        string osName = osVersion.Build >= 22000 ? "Windows 11" : "Windows";
        Log.Info("App", $"{osName} {osVersion.Major}.{osVersion.Minor}.{osVersion.Build}");
        Log.Info("App", $"NativeAOT={!RuntimeFeature.IsDynamicCodeCompiled}");
    }
}

