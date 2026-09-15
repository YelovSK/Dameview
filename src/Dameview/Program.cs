using System.Runtime.CompilerServices;
using Dameview.App;
using Dameview.Diagnostics;
using Dameview.Installation;
using Dameview.Updates;
using Dameview.Win32;

namespace Dameview;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        InitializeLogger();
        NativeMethods.EnablePerMonitorDpiAwareness();
        NativeMethods.InitializeComApartment(ComApartment.ApartmentThreaded);

        try
        {
            if (AppUpdateApplier.TryApply(args))
            {
                return 0;
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

            using var instance = SingleInstanceHost.AcquireOrForward(args);
            if (instance is null)
            {
                return 0;
            }

            using var app = new DameviewApp();
            return app.Run(args);
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
        Log.Info("App", $"Dameview {AppInstallation.GetDisplayVersion(Environment.ProcessPath)}");
        Version osVersion = Environment.OSVersion.Version;
        string osName = osVersion.Build >= 22000 ? "Windows 11" : "Windows";
        Log.Info("App", $"{osName} {osVersion.Major}.{osVersion.Minor}.{osVersion.Build}");
        Log.Info("App", $"NativeAOT={!RuntimeFeature.IsDynamicCodeCompiled}");
    }
}

