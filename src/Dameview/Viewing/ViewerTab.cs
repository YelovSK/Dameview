namespace Dameview.Viewing;

// Owns one viewing session.
internal sealed class ViewerTab : IDisposable
{
    private bool _disposed;

    internal ViewerTab(ViewerSession session) => Session = session;

    internal event Action<ViewerTab>? Disposed;

    internal ViewerSession Session { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Session.Dispose();
        Disposed?.Invoke(this);
        Disposed = null;
    }
}
