using System.Runtime.InteropServices;
using Dameview.Platform;

namespace Dameview;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        NativeMethods.EnablePerMonitorDpiAwareness();
        NativeMethods.InitializeComApartment();

        try
        {
            using var app = new DameviewApp();
            return app.Run(args);
        }
        finally
        {
            NativeMethods.UninitializeComApartment();
        }
    }
}

