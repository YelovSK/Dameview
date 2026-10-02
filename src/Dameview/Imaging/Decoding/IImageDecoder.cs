using Dameview.Imaging.Loading;
using Dameview.Win32;

namespace Dameview.Imaging.Decoding;

/// <summary>Reads metadata and decodes still images into CPU-backed pixel data.</summary>
internal interface IImageDecoder : IDisposable
{
    /// <summary>Reads the source dimensions without decoding its pixels.</summary>
    public ImageInfo GetInfo(string path);

    /// <summary>Decodes the image at <paramref name="path"/> into a disposable upload buffer.</summary>
    public DecodedImageUpload DecodeUpload(
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>Decodes the image at <paramref name="path"/> as it is shown, ready for the clipboard.</summary>
    public ClipboardBitmap DecodeClipboardBitmap(
        string path,
        CancellationToken cancellationToken = default);
}
