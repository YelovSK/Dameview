using System.Drawing;

namespace Dameview.Viewing;

internal sealed class ImageViewport
{
    private const float MaximumScale = 64.0f;
    private const double ZoomStep = 1.2;

    private float _viewportWidth;
    private float _viewportHeight;
    private float _imageWidth;
    private float _imageHeight;
    private float _centerX;
    private float _centerY;

    internal ImageViewport(int viewportWidth, int viewportHeight)
    {
        SetViewportSize(viewportWidth, viewportHeight);
    }

    internal ViewportMode Mode { get; private set; } = ViewportMode.Fit;
    internal float Scale { get; private set; } = 1.0f;
    internal PointF Center => new(_centerX, _centerY);
    internal PointF ImageCenter => new(_imageWidth / 2.0f, _imageHeight / 2.0f);
    internal PointF ViewportCenter => new(_viewportWidth / 2.0f, _viewportHeight / 2.0f);
    internal float FitScale => GetFitScale();

    internal void SetViewportSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        _viewportWidth = width;
        _viewportHeight = height;

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

    internal void SetImageSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _imageWidth = width;
        _imageHeight = height;
        Fit();
    }

    internal void Fit()
    {
        Mode = ViewportMode.Fit;
        Scale = GetFitScale();
        CenterImage();
    }

    internal float GetZoomScale(float scale, int wheelDelta)
    {
        if (!HasImage || wheelDelta == 0)
        {
            return scale;
        }

        float zoomFactor = (float)Math.Pow(ZoomStep, wheelDelta / 120.0);
        return Math.Clamp(scale * zoomFactor, GetFitScale(), MaximumScale);
    }

    internal void SetScaleAt(
        float scale,
        float viewportX,
        float viewportY,
        PointF imagePosition)
    {
        if (!HasImage)
        {
            return;
        }

        float fitScale = GetFitScale();
        float newScale = Math.Clamp(scale, fitScale, MaximumScale);

        if (newScale == fitScale)
        {
            Fit();
            return;
        }

        Scale = newScale;
        PointF center = GetCenterAtScale(
            Scale,
            viewportX,
            viewportY,
            imagePosition);
        _centerX = center.X;
        _centerY = center.Y;
        Mode = ViewportMode.Custom;
    }

    internal PointF GetCenterAtScale(
        float scale,
        float viewportX,
        float viewportY,
        PointF imagePosition)
    {
        if (!HasImage)
        {
            return Center;
        }

        float clampedScale = Math.Clamp(scale, GetFitScale(), MaximumScale);
        var center = new PointF(
            imagePosition.X - ((viewportX - (_viewportWidth / 2.0f)) / clampedScale),
            imagePosition.Y - ((viewportY - (_viewportHeight / 2.0f)) / clampedScale));
        return ClampCenter(center, clampedScale);
    }

    internal void SetAnimatedTransform(float scale, PointF center)
    {
        if (!HasImage)
        {
            return;
        }

        Scale = Math.Clamp(scale, GetFitScale(), MaximumScale);
        _centerX = center.X;
        _centerY = center.Y;
        Mode = ViewportMode.Custom;
    }

    internal void SetActualSizeAt(
        float viewportX,
        float viewportY,
        PointF imagePosition)
    {
        if (!HasImage)
        {
            return;
        }

        SetScaleAt(1.0f, viewportX, viewportY, imagePosition);
        Mode = ViewportMode.ActualSize;
    }

    internal void PanBy(float deltaX, float deltaY)
    {
        if (!HasImage
            || ((_imageWidth * Scale) <= _viewportWidth
                && (_imageHeight * Scale) <= _viewportHeight))
        {
            return;
        }

        _centerX -= deltaX / Scale;
        _centerY -= deltaY / Scale;
        Mode = ViewportMode.Custom;
        ClampCenter();
    }

    internal RectangleF GetDestinationRectangle()
    {
        float width = _imageWidth * Scale;
        float height = _imageHeight * Scale;
        float x = (_viewportWidth / 2.0f) - (_centerX * Scale);
        float y = (_viewportHeight / 2.0f) - (_centerY * Scale);
        return new RectangleF(x, y, width, height);
    }

    internal PointF ViewportToImage(float viewportX, float viewportY)
    {
        float imageX = _centerX + ((viewportX - (_viewportWidth / 2.0f)) / Scale);
        float imageY = _centerY + ((viewportY - (_viewportHeight / 2.0f)) / Scale);
        return new PointF(imageX, imageY);
    }

    internal bool HasImage => _imageWidth > 0.0f && _imageHeight > 0.0f;

    private float GetFitScale()
    {
        if (!HasImage || _viewportWidth <= 0.0f || _viewportHeight <= 0.0f)
        {
            return 1.0f;
        }

        return Math.Min(
            1.0f,
            Math.Min(_viewportWidth / _imageWidth, _viewportHeight / _imageHeight));
    }

    private void CenterImage()
    {
        _centerX = _imageWidth / 2.0f;
        _centerY = _imageHeight / 2.0f;
    }

    private void ClampCenter()
    {
        PointF center = ClampCenter(Center, Scale);
        _centerX = center.X;
        _centerY = center.Y;
    }

    private PointF ClampCenter(PointF center, float scale)
    {
        return new PointF(
            ClampAxis(center.X, _imageWidth, _viewportWidth, scale),
            ClampAxis(center.Y, _imageHeight, _viewportHeight, scale));
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
