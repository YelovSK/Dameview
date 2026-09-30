using System.Drawing;
using System.Numerics;

namespace Dameview.Viewing;

internal sealed class ImageViewport
{
    private const float MaximumScale = 64.0f;
    private const double ZoomStep = 1.2;

    private SizeF _viewportSize;
    private SizeF _imageSize;
    private PointF _center;

    internal ImageViewport(Size viewportSize)
    {
        SetViewportSize(viewportSize);
    }

    internal ViewportMode Mode { get; private set; } = ViewportMode.Fit;
    internal float Scale { get; private set; } = 1.0f;
    internal PointF Center => _center;
    internal PointF ImageCenter => new(_imageSize.Width / 2.0f, _imageSize.Height / 2.0f);
    internal PointF ViewportCenter => new(_viewportSize.Width / 2.0f, _viewportSize.Height / 2.0f);
    internal float FitScale => GetFitScale();

    internal void SetViewportSize(Size size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size.Width);
        ArgumentOutOfRangeException.ThrowIfNegative(size.Height);

        _viewportSize = size;

        if (!HasImage)
        {
            return;
        }

        if (Mode == ViewportMode.Fit)
        {
            Fit();
        }
        else
        {
            ClampCenter();
        }
    }

    internal void SetImageSize(Size size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Height);

        _imageSize = size;
        Fit();
    }

    internal void Fit()
    {
        Mode = ViewportMode.Fit;
        Scale = GetFitScale();
        _center = ImageCenter;
    }

    internal float GetZoomScale(float scale, int wheelDelta)
    {
        if (!HasImage || wheelDelta == 0)
        {
            return scale;
        }

        float zoomFactor = (float)Math.Pow(ZoomStep, wheelDelta / 120.0);
        return Math.Clamp(scale * zoomFactor, GetMinimumScale(), MaximumScale);
    }

    /// <summary>Changes the scale while keeping <paramref name="imagePoint"/> under <paramref name="viewportPoint"/>.</summary>
    internal void SetScaleAt(float scale, PointF viewportPoint, PointF imagePoint)
    {
        if (!HasImage)
        {
            return;
        }

        float fitScale = GetFitScale();
        float newScale = Math.Clamp(scale, GetMinimumScale(), MaximumScale);

        if (newScale == fitScale)
        {
            Fit();
            return;
        }

        Scale = newScale;
        _center = GetCenterAtScale(Scale, viewportPoint, imagePoint);
        Mode = ViewportMode.Custom;
    }

    internal PointF GetCenterAtScale(float scale, PointF viewportPoint, PointF imagePoint)
    {
        if (!HasImage)
        {
            return _center;
        }

        float clampedScale = Math.Clamp(scale, GetMinimumScale(), MaximumScale);
        PointF center = imagePoint - ((viewportPoint - ViewportCenter) / clampedScale);
        return ClampCenter(center, clampedScale);
    }

    internal void SetAnimatedTransform(float scale, PointF center)
    {
        if (!HasImage)
        {
            return;
        }

        Scale = Math.Clamp(scale, GetMinimumScale(), MaximumScale);
        _center = center;
        Mode = ViewportMode.Custom;
    }

    internal void SetActualSizeAt(PointF viewportPoint, PointF imagePoint)
    {
        if (!HasImage)
        {
            return;
        }

        SetScaleAt(1.0f, viewportPoint, imagePoint);
        Mode = ViewportMode.ActualSize;
    }

    internal void PanBy(Vector2 delta)
    {
        if (!HasImage
            || ((_imageSize.Width * Scale) <= _viewportSize.Width
                && (_imageSize.Height * Scale) <= _viewportSize.Height))
        {
            return;
        }

        _center -= delta / Scale;
        Mode = ViewportMode.Custom;
        ClampCenter();
    }

    internal RectangleF GetDestinationRectangle()
    {
        // We want _center to show up in the middle of the viewport.
        // The image's top-left corner is then that far up and left of the middle, times the zoom.
        PointF location = ViewportCenter - ((_center - PointF.Empty) * Scale);
        return new RectangleF(location, _imageSize * Scale);
    }

    internal PointF ViewportToImage(PointF viewportPoint) => _center + ((viewportPoint - ViewportCenter) / Scale);

    internal bool HasImage => _imageSize.Width > 0.0f && _imageSize.Height > 0.0f;

    private float GetFitScale()
    {
        if (!HasImage || _viewportSize.Width <= 0.0f || _viewportSize.Height <= 0.0f)
        {
            return 1.0f;
        }

        return Math.Min(_viewportSize.Width / _imageSize.Width, _viewportSize.Height / _imageSize.Height);
    }

    private float GetMinimumScale() => Math.Min(1.0f, GetFitScale());

    private void ClampCenter() => _center = ClampCenter(_center, Scale);

    private PointF ClampCenter(PointF center, float scale)
    {
        return new PointF(
            ClampAxis(center.X, _imageSize.Width, _viewportSize.Width, scale),
            ClampAxis(center.Y, _imageSize.Height, _viewportSize.Height, scale));
    }

    private static float ClampAxis(
        float center,
        float imageSize,
        float viewportSize,
        float scale)
    {
        if ((imageSize * scale) <= viewportSize)
        {
            return imageSize / 2.0f;
        }

        float halfVisibleSize = viewportSize / (2.0f * scale);
        return Math.Clamp(center, halfVisibleSize, imageSize - halfVisibleSize);
    }
}
