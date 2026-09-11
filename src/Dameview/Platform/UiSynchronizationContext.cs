namespace Dameview.Platform;

// A SynchronizationContext backed by the application's UI message loop. Continuations captured
// on the UI thread resume there after an await; background code can stay on the thread pool.
internal sealed class UiSynchronizationContext(Action<Action> post) : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        post(() => d(state));
    }

    public override SynchronizationContext CreateCopy() => this;
}
