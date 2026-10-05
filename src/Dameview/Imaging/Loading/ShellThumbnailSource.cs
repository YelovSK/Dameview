namespace Dameview.Imaging.Loading;

/// <summary>Thumbnails from the Windows shell, which are quick and often cached by Windows.</summary>
internal sealed class ShellThumbnailSource(IImageLoadingBackend backend) : IImageSource
{
    public string WorkerName => "Dameview thumbnails";
    public int WorkerCount => 4;

    public object? CreateWorkerState() => null;

    public ImageRepresentation? Load(object? workerState, string path, ImageLoadContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        DecodedImageUpload thumbnail = backend.LoadThumbnail(path)
            ?? throw new InvalidDataException("Windows has no thumbnail for this file.");
        return new UploadImageRepresentation(thumbnail);
    }
}
