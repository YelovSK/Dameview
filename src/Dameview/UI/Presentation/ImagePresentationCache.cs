using System.Drawing;
using System.Numerics;
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
    private Matrix3x2 _placement;
    private System.Drawing.Size _viewportSize;
    private Point _offsetPixels;
    private float _dpi;

    internal ImagePresentationCache(ID2D1DeviceContext deviceContext)
    {
        _renderContext = D2DBitmapFactory.CreateOffscreenContext(deviceContext);
    }

    internal void Recreate(ID2D1DeviceContext deviceContext)
    {
        Clear();
        _renderContext.Dispose();
        _renderContext = D2DBitmapFactory.CreateOffscreenContext(deviceContext);
    }

    /// <summary>The rescale already held for this exact presentation, if there is one.</summary>
    /// <param name="placement">Maps source pixels onto viewport pixels.</param>
    internal ImagePresentation? TryGet(
        ID2D1Bitmap1 source,
        Matrix3x2 placement,
        System.Drawing.Size viewportSize,
        float dpi)
    {
        return _bitmap is not null
            && ReferenceEquals(_source, source)
            && _placement == placement
            && _viewportSize == viewportSize
            && _dpi == dpi
                ? new ImagePresentation(_bitmap, _offsetPixels)
                : null;
    }

    /// <param name="placement">
    /// Maps source pixels onto viewport pixels. It may scale, move, mirror and turn by quarter turns.
    /// </param>
    internal ImagePresentation? GetOrCreate(
        ID2D1Bitmap1 source,
        Matrix3x2 placement,
        System.Drawing.Size viewportSize,
        float dpi)
    {
        if (TryGet(source, placement, viewportSize, dpi) is { } cached)
        {
            return cached;
        }

        var corner = Vector2.Transform(Vector2.Zero, placement);
        var oppositeCorner = Vector2.Transform(
            new Vector2(source.PixelSize.Width, source.PixelSize.Height),
            placement);
        var imageBounds = RectangleF.FromLTRB(
            MathF.Min(corner.X, oppositeCorner.X),
            MathF.Min(corner.Y, oppositeCorner.Y),
            MathF.Max(corner.X, oppositeCorner.X),
            MathF.Max(corner.Y, oppositeCorner.Y));

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
        bool upscales = new Vector2(placement.M11, placement.M12).Length() > 1.0f;

        var pixelSize = new SizeI(right - left, bottom - top);
        ID2D1Bitmap1 bitmap = D2DBitmapFactory.CreateScaled(
            _renderContext,
            source,
            pixelSize,
            dpi,
            placement
                * Matrix3x2.CreateTranslation(-left, -top)
                * Matrix3x2.CreateScale(UiDpi.PixelsToDips(1.0f, dpi)),
            upscales ? UpscaleSharpness : DownscaleSharpness);
        Clear();
        _bitmap = bitmap;
        _source = source;
        _placement = placement;
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
        _placement = default;
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
