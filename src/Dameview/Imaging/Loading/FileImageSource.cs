using Dameview.Diagnostics;
using Dameview.Imaging.Animation;
using Dameview.Imaging.Decoding;

namespace Dameview.Imaging.Loading;

/// <summary>Full images, decoded from their files.</summary>
internal sealed class FileImageSource(
    IImageLoadingBackend backend,
    ImageRepresentationPolicy representationPolicy) : IImageSource
{
    public string WorkerName => "Dameview image loader";

    // So that two images shown side by side load side by side.
    public int WorkerCount => 2;

    public object? CreateWorkerState() => backend.CreateDecoder();

    public ImageRepresentation? Load(object? workerState, string path, ImageLoadContext context)
    {
        var decoder = (IImageDecoder)workerState!;
        CancellationToken cancellationToken = context.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        using IOpenedImage opened = decoder.Open(path);
        ImageInfo info = opened.Info;
        ImageRepresentationKind kind = representationPolicy.Select(info, backend.SupportsAnimation(path));
        long? cachedBytes = kind == ImageRepresentationKind.Static
            ? DecodedImage.GetByteCount(info.Width, info.Height)
            : null;
        if (!context.ShouldLoad(cachedBytes))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        switch (kind)
        {
            case ImageRepresentationKind.Animated:
                return OpenAnimation(path);
            case ImageRepresentationKind.Tiled:
                Log.Debug("Image", $"Selected tiled representation for '{path}'.");
                return new TiledImageRepresentation(backend.OpenTiledImage(path));
            default:
                Log.Debug("Image", $"Selected static representation for '{path}'.");
                return new UploadImageRepresentation(opened.DecodeUpload(cancellationToken));
        }
    }

    private ImageRepresentation OpenAnimation(string path)
    {
        IAnimationSession? animation = backend.OpenAnimation(path);
        try
        {
            if (!animation.IsAnimated)
            {
                Log.Debug("Image", $"Selected static representation for '{path}'.");
                return new DecodedImageRepresentation(animation.FirstFrame.Image);
            }

            var representation = new AnimatedImageRepresentation(animation);
            Log.Debug("Image", $"Selected animated representation for '{path}'.");
            animation = null;
            return representation;
        }
        finally
        {
            animation?.Dispose();
        }
    }
}
