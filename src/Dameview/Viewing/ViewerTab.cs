using Dameview.Imaging;
using Dameview.Navigation;
using Dameview.UI;

namespace Dameview.Viewing;

// Owns the per-tab services borrowed by ViewerSession and its presentation UI.
internal sealed class ViewerTab : IDisposable
{
    private readonly IFolderMonitor? _folderMonitor;
    private readonly PresentationImageLoader? _imageLoader;
    private readonly ImageLoadClient? _loadClient;

    internal ViewerTab(
        ViewerSession session,
        IFolderMonitor folderMonitor,
        PresentationImageLoader imageLoader,
        ImageLoadClient loadClient)
        : this(session)
    {
        _folderMonitor = folderMonitor;
        _imageLoader = imageLoader;
        _loadClient = loadClient;
    }

    internal ViewerTab(ViewerSession session) => Session = session;

    internal ViewerSession Session { get; }

    public void Dispose()
    {
        Session.Dispose();
        _loadClient?.Dispose();
        _folderMonitor?.Dispose();
        _imageLoader?.Dispose();
    }
}
