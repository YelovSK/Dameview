namespace Dameview.App;

using Dameview.Win32;

internal enum SingleInstanceCommand : ulong
{
    Activate = 1,
    Open = 2,
}

internal sealed class SingleInstanceHost : IDisposable
{
    private const string MutexName = @"Local\Dameview.Instance";
    private const int ForwardAttempts = 20;
    private const int ForwardRetryDelayMilliseconds = 100;

    private readonly Mutex _mutex;

    private SingleInstanceHost(Mutex mutex) => _mutex = mutex;

    internal static SingleInstanceHost? AcquireOrForward(IReadOnlyList<string> paths)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (createdNew)
        {
            return new SingleInstanceHost(mutex);
        }

        mutex.Dispose();
        Forward(paths);
        return null;
    }

    public void Dispose() => _mutex.Dispose();

    private static void Forward(IReadOnlyList<string> paths)
    {
        nuint command = paths.Count == 0
            ? (nuint)SingleInstanceCommand.Activate
            : (nuint)SingleInstanceCommand.Open;
        string payload = paths.Count == 0 ? string.Empty : string.Join('\n', paths);

        for (int attempt = 0; attempt < ForwardAttempts; attempt++)
        {
            if (WindowCopyData.TrySend(AppWindow.WindowClassName, command, payload))
            {
                return;
            }

            Thread.Sleep(ForwardRetryDelayMilliseconds);
        }

        throw new IOException("Could not find the running Dameview window.");
    }
}
