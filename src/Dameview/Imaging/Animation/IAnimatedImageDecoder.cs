namespace Dameview.Imaging;

/// <summary>Decodes image formats that may contain animated frames.</summary>
internal interface IAnimatedImageDecoder
{
    /// <summary>Determines whether this decoder recognizes the file.</summary>
    /// <returns><see langword="true"/> when the decoder can open the path.</returns>
    public bool CanDecode(string path);

    /// <summary>Opens a progressive animation session; the caller owns the returned session.</summary>
    public IAnimationSession Open(string path);
}
