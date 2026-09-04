namespace Dameview.Imaging;

// Load completions are dispatched to the UI thread. Superseded requests are
// suppressed, including completions that were already queued for delivery.
internal interface IImageLoader
{
    public void Load(string path, Action<ImageLoadResult> completed);
    public void Preload(IEnumerable<string?> paths);
}
