using Dameview.Imaging.Loading;
using Dameview.Rendering;
using Dameview.UI.Foundation;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

/// <summary>A visible gallery item's thumbnail and the scaled copy of it that gets drawn.</summary>
internal sealed class GalleryItemSlot : IDisposable
{
    private const float ThumbnailSharpness = 1.0f;

    private SizeI _displayPixelSize;
    private float _displayDpi;
    private ID2D1Bitmap1? _displayBitmap;
    private CachedBitmapLease? _sourceLease;

    internal IDisposable? Request { get; set; }
    internal ID2D1Bitmap1? SourceBitmap => _sourceLease?.Bitmap;

    internal void SetSourceBitmap(CachedBitmapLease lease)
    {
        Request?.Dispose();
        Request = null;
        _sourceLease?.Dispose();
        _sourceLease = lease;
        ClearDisplayBitmap();
    }

    /// <summary>Returns the last resampled thumbnail if it still fits the requested size.</summary>
    internal ID2D1Bitmap1? TryGetDisplayBitmap(float width, float height, float dpi) =>
        _displayPixelSize == ToPixelSize(width, height, dpi) && _displayDpi == dpi ? _displayBitmap : null;

    /// <summary>Returns the thumbnail resampled to the requested size, reusing the last one when it still fits.</summary>
    internal ID2D1Bitmap1 GetDisplayBitmap(
        ID2D1DeviceContext scaleContext,
        float width,
        float height,
        float dpi)
    {
        if (TryGetDisplayBitmap(width, height, dpi) is { } current)
        {
            return current;
        }

        ID2D1Bitmap1 source = SourceBitmap
            ?? throw new InvalidOperationException("The thumbnail has not been loaded.");
        SizeI pixelSize = ToPixelSize(width, height, dpi);
        ID2D1Bitmap1 bitmap = D2DBitmapFactory.CreateScaled(
            scaleContext,
            source,
            pixelSize,
            dpi,
            ThumbnailSharpness);
        ClearDisplayBitmap();
        _displayBitmap = bitmap;
        _displayPixelSize = pixelSize;
        _displayDpi = dpi;
        return bitmap;
    }

    public void Dispose()
    {
        Request?.Dispose();
        ClearDisplayBitmap();
        _sourceLease?.Dispose();
    }

    /// <summary>Drops the scaled copy; the next draw rebuilds it from the thumbnail.</summary>
    internal void ClearDisplayBitmap()
    {
        _displayBitmap?.Dispose();
        _displayBitmap = null;
        _displayPixelSize = default;
        _displayDpi = 0.0f;
    }

    private static SizeI ToPixelSize(float width, float height, float dpi) => new(
        Math.Max(1, (int)MathF.Round(UiDpi.DipsToPixels(width, dpi))),
        Math.Max(1, (int)MathF.Round(UiDpi.DipsToPixels(height, dpi))));
}
