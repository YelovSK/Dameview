namespace Dameview.Imaging;

/// <summary>Platform-specific image operations used by the loading coordinator.</summary>
internal interface IImageLoadingBackend
{
    /// <summary>Creates a decoder whose lifetime is owned by the caller.</summary>
    public IImageDecoder CreateDecoder();

    /// <summary>Loads a best-effort shell or reduced-size thumbnail; returns <see langword="null"/> when unavailable.</summary>
    public DecodedImage? LoadThumbnail(string path);

    /// <summary>Determines whether an animation decoder is available for the path.</summary>
    /// <returns><see langword="true"/> when <see cref="OpenAnimation"/> can be used.</returns>
    public bool SupportsAnimation(string path);

    /// <summary>Opens an animation session. The caller owns the returned session.</summary>
    public IAnimationSession OpenAnimation(string path);
}
