namespace Dameview.Platform;

// A SynchronizationContext backed by the window's message loop. Continuations captured
// on the window thread resume there after an await; background code can stay on the thread pool.
internal sealed class WindowSynchronizationContext(Action<Action> post) : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        post(() => d(state));
    }

    public override SynchronizationContext CreateCopy() => this;
}
