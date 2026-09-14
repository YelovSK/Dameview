using Dameview.Installation;
using Dameview.Updates;
using Dameview.Win32;

namespace Dameview;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
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

            using var app = new DameviewApp();
            return app.Run(args);
        }
        finally
        {
            NativeMethods.UninitializeComApartment();
        }
    }
}

