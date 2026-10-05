using Dameview.Imaging.Loading;
using Dameview.Win32;

namespace Dameview.Imaging.Decoding;

/// <summary>Reads metadata and decodes still images into CPU-backed pixel data.</summary>
internal interface IImageDecoder : IDisposable
{
    /// <summary>
    /// Reads the image's header, keeping the file open so the pixels can be decoded without
    /// reading it again. Some codecs do real work just to report the header.
    /// </summary>
    public IOpenedImage Open(string path);

    /// <summary>Decodes the image at <paramref name="path"/> as it is shown, ready for the clipboard.</summary>
    public ClipboardBitmap DecodeClipboardBitmap(
        string path,
        CancellationToken cancellationToken = default);
}

internal interface IOpenedImage : IDisposable
{
    public ImageInfo Info { get; }

    /// <summary>Decodes the pixels into a disposable upload buffer.</summary>
    public DecodedImageUpload DecodeUpload(CancellationToken cancellationToken = default);
}
