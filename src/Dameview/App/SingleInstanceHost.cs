namespace Dameview.App;

using Dameview.Win32;

internal enum SingleInstanceCommand : ulong
{
    Activate = 1,
    Open = 2,
}

internal sealed record ForwardedToRunningCopy;

// Another copy holds the instance, but its window never answered.
internal sealed record RunningCopyUnreachable;

internal readonly union SingleInstanceResult(SingleInstanceHost, ForwardedToRunningCopy, RunningCopyUnreachable);

internal sealed class SingleInstanceHost : IDisposable
{
    private const string MutexName = @"Local\Dameview.Instance";
    private const int ForwardAttempts = 20;
    private const int ForwardRetryDelayMilliseconds = 100;

    private readonly Mutex _mutex;

    private SingleInstanceHost(Mutex mutex) => _mutex = mutex;

    internal static SingleInstanceResult AcquireOrForward(IReadOnlyList<string> paths)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (createdNew)
        {
            return new SingleInstanceHost(mutex);
        }

        mutex.Dispose();
        return TryForward(paths) ? new ForwardedToRunningCopy() : new RunningCopyUnreachable();
    }

    public void Dispose() => _mutex.Dispose();

    private static bool TryForward(IReadOnlyList<string> paths)
    {
        nuint command = paths.Count == 0
            ? (nuint)SingleInstanceCommand.Activate
            : (nuint)SingleInstanceCommand.Open;
        string payload = paths.Count == 0 ? string.Empty : string.Join('\n', paths);

        for (int attempt = 0; attempt < ForwardAttempts; attempt++)
        {
            if (WindowCopyData.TrySend(AppWindow.WindowClassName, command, payload))
            {
                return true;
            }

            Thread.Sleep(ForwardRetryDelayMilliseconds);
        }

        return false;
    }
}
