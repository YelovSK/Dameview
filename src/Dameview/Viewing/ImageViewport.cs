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

    internal void ShowActualSize()
    {
        if (!HasImage)
        {
            return;
        }

        Mode = ViewportMode.ActualSize;
        Scale = 1.0f;
        CenterImage();
    }

    internal void ToggleFitAndActualSize()
    {
        if (Mode == ViewportMode.Fit)
        {
            ShowActualSize();
        }
        else
        {
            Fit();
        }
    }

    internal void ZoomAt(float viewportX, float viewportY, int wheelDelta)
    {
        if (!HasImage || wheelDelta == 0)
        {
            return;
        }

        PointF imagePosition = ViewportToImage(viewportX, viewportY);
        float fitScale = GetFitScale();
        float zoomFactor = (float)Math.Pow(ZoomStep, wheelDelta / 120.0);
        float newScale = Math.Clamp(Scale * zoomFactor, fitScale, MaximumScale);

        if (newScale == fitScale)
        {
            Fit();
            return;
        }

        Scale = newScale;
        _centerX = imagePosition.X - ((viewportX - (_viewportWidth / 2.0f)) / Scale);
        _centerY = imagePosition.Y - ((viewportY - (_viewportHeight / 2.0f)) / Scale);
        Mode = ViewportMode.Custom;
        ClampCenter();
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

    internal RectangleF GetVisibleImageRectangle()
    {
        if (!HasImage)
        {
            return RectangleF.Empty;
        }

        float halfVisibleWidth = _viewportWidth / (2.0f * Scale);
        float halfVisibleHeight = _viewportHeight / (2.0f * Scale);
        float left = Math.Max(0.0f, _centerX - halfVisibleWidth);
        float top = Math.Max(0.0f, _centerY - halfVisibleHeight);
        float right = Math.Min(_imageWidth, _centerX + halfVisibleWidth);
        float bottom = Math.Min(_imageHeight, _centerY + halfVisibleHeight);
        return RectangleF.FromLTRB(left, top, right, bottom);
    }

    internal PointF ViewportToImage(float viewportX, float viewportY)
    {
        float imageX = _centerX + ((viewportX - (_viewportWidth / 2.0f)) / Scale);
        float imageY = _centerY + ((viewportY - (_viewportHeight / 2.0f)) / Scale);
        return new PointF(imageX, imageY);
    }

    private bool HasImage => _imageWidth > 0.0f && _imageHeight > 0.0f;

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
        _centerX = ClampAxis(_centerX, _imageWidth, _viewportWidth);
        _centerY = ClampAxis(_centerY, _imageHeight, _viewportHeight);
    }

    private float ClampAxis(float center, float imageSize, float viewportSize)
    {
        if ((imageSize * Scale) <= viewportSize)
        {
            return imageSize / 2.0f;
        }

        float halfVisibleSize = viewportSize / (2.0f * Scale);
        return Math.Clamp(center, halfVisibleSize, imageSize - halfVisibleSize);
    }
}
