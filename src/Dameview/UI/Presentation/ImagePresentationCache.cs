using System.Drawing;
using Dameview.Rendering;
using Dameview.UI.Foundation;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Dameview.UI.Presentation;

// Retains the expensive high-quality rasterization for an unchanged static viewport.
internal sealed class ImagePresentationCache : IDisposable
{
    private const float DownscaleSharpness = 0.7f;
    private const float UpscaleSharpness = 0.25f;

    private ID2D1DeviceContext _renderContext;
    private ID2D1Bitmap1? _bitmap;
    private ID2D1Bitmap1? _source;
    private RectangleF _imageBounds;
    private System.Drawing.Size _viewportSize;
    private Point _offsetPixels;
    private float _dpi;

    internal ImagePresentationCache(ID2D1DeviceContext deviceContext)
    {
        _renderContext = CreateRenderContext(deviceContext);
    }

    /// <summary>Rebuilds against a replacement device, discarding the rescale held for the old one.</summary>
    internal void Recreate(ID2D1DeviceContext deviceContext)
    {
        Clear();
        _renderContext.Dispose();
        _renderContext = CreateRenderContext(deviceContext);
    }

    private static ID2D1DeviceContext CreateRenderContext(ID2D1DeviceContext deviceContext)
    {
        using ID2D1Device device = deviceContext.Device;
        return device.CreateDeviceContext();
    }

    /// <summary>The rescale already held for this exact presentation, if there is one.</summary>
    internal ImagePresentation? TryGet(
        ID2D1Bitmap1 source,
        RectangleF imageBounds,
        System.Drawing.Size viewportSize,
        float dpi)
    {
        return _bitmap is not null
            && ReferenceEquals(_source, source)
            && _imageBounds == imageBounds
            && _viewportSize == viewportSize
            && _dpi == dpi
                ? new ImagePresentation(_bitmap, _offsetPixels)
                : null;
    }

    internal ImagePresentation? GetOrCreate(
        ID2D1Bitmap1 source,
        RectangleF imageBounds,
        System.Drawing.Size viewportSize,
        float dpi)
    {
        if (TryGet(source, imageBounds, viewportSize, dpi) is { } cached)
        {
            return cached;
        }

        int left = Math.Clamp((int)MathF.Floor(imageBounds.Left), 0, viewportSize.Width);
        int top = Math.Clamp((int)MathF.Floor(imageBounds.Top), 0, viewportSize.Height);
        int right = Math.Clamp((int)MathF.Ceiling(imageBounds.Right), 0, viewportSize.Width);
        int bottom = Math.Clamp((int)MathF.Ceiling(imageBounds.Bottom), 0, viewportSize.Height);
        if (right <= left || bottom <= top)
        {
            Clear();
            return null;
        }

        // Cubic can produce halos when zoomed beyond 100%, so lower the sharpness in that case.
        bool upscales = imageBounds.Width > source.PixelSize.Width;

        var pixelSize = new SizeI(right - left, bottom - top);
        ID2D1Bitmap1 bitmap = D2DBitmapFactory.CreateScaled(
            _renderContext,
            source,
            pixelSize,
            dpi,
            new Rect(
                UiDpi.PixelsToDips(imageBounds.X - left, dpi),
                UiDpi.PixelsToDips(imageBounds.Y - top, dpi),
                UiDpi.PixelsToDips(imageBounds.Width, dpi),
                UiDpi.PixelsToDips(imageBounds.Height, dpi)),
            upscales ? UpscaleSharpness : DownscaleSharpness);
        Clear();
        _bitmap = bitmap;
        _source = source;
        _imageBounds = imageBounds;
        _viewportSize = viewportSize;
        _offsetPixels = new Point(left, top);
        _dpi = dpi;
        return new ImagePresentation(bitmap, _offsetPixels);
    }

    internal void Clear()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _source = null;
        _imageBounds = default;
        _viewportSize = default;
        _offsetPixels = default;
        _dpi = 0.0f;
    }

    public void Dispose()
    {
        Clear();
        _renderContext.Dispose();
    }
}

internal readonly record struct ImagePresentation(
    ID2D1Bitmap1 Bitmap,
    Point OffsetPixels);
