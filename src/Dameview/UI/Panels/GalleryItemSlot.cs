using Dameview.Rendering;
using Dameview.UI.Foundation;
using Dameview.UI.Presentation;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

/// <summary>Everything one visible gallery item owns: its thumbnail, a scaled copy of it, and its label.</summary>
internal sealed class GalleryItemSlot : IDisposable
{
    internal const float LabelHeight = 24.0f;

    private const float ThumbnailSharpness = 1.0f;

    private float _labelWidth;
    private SizeI _displayPixelSize;
    private float _displayDpi;
    private ID2D1Bitmap1? _displayBitmap;
    private CachedBitmapLease? _sourceLease;

    internal GalleryItemSlot(
        IDWriteFactory directWriteFactory,
        IDWriteTextFormat labelFormat,
        string label,
        float labelWidth)
    {
        LabelLayout = directWriteFactory.CreateTextLayout(label, labelFormat, labelWidth, LabelHeight);
        _labelWidth = labelWidth;
    }

    internal IDisposable? Request { get; set; }
    internal ID2D1Bitmap1? SourceBitmap => _sourceLease?.Bitmap.Bitmap;
    internal IDWriteTextLayout LabelLayout { get; private set; }

    internal void SetSourceBitmap(CachedBitmapLease lease)
    {
        _sourceLease?.Dispose();
        _sourceLease = lease;
        ClearDisplayBitmap();
    }

    /// <summary>Returns the thumbnail resampled to the requested size, reusing the last one when it still fits.</summary>
    internal ID2D1Bitmap1 GetDisplayBitmap(
        ID2D1DeviceContext scaleContext,
        float width,
        float height,
        float dpi)
    {
        ID2D1Bitmap1 source = SourceBitmap
            ?? throw new InvalidOperationException("The thumbnail has not been loaded.");
        var pixelSize = new SizeI(
            Math.Max(1, (int)MathF.Round(UiDpi.DipsToPixels(width, dpi))),
            Math.Max(1, (int)MathF.Round(UiDpi.DipsToPixels(height, dpi))));
        if (_displayBitmap is not null && _displayPixelSize == pixelSize && _displayDpi == dpi)
        {
            return _displayBitmap;
        }

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

    internal void SetLabelLayout(
        IDWriteFactory directWriteFactory,
        IDWriteTextFormat labelFormat,
        string label,
        float labelWidth)
    {
        if (_labelWidth == labelWidth)
        {
            return;
        }

        LabelLayout.Dispose();
        LabelLayout = directWriteFactory.CreateTextLayout(label, labelFormat, labelWidth, LabelHeight);
        _labelWidth = labelWidth;
    }

    public void Dispose()
    {
        Request?.Dispose();
        ClearDisplayBitmap();
        _sourceLease?.Dispose();
        LabelLayout.Dispose();
    }

    private void ClearDisplayBitmap()
    {
        _displayBitmap?.Dispose();
        _displayBitmap = null;
        _displayPixelSize = default;
        _displayDpi = 0.0f;
    }
}
