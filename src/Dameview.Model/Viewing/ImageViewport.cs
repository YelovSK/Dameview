using System.Drawing;
using System.Numerics;
using Dameview.Imaging;

namespace Dameview.Viewing;

// Works in image coordinates of the oriented image, so turning it needs no special cases
// beyond the final transform from stored pixels.
internal sealed class ImageViewport
{
    private const float MaximumScale = 64.0f;
    private const double ZoomStep = 1.2;

    private SizeF _viewportSize;
    private SizeF _storedImageSize;
    private SizeF _imageSize;
    private PointF _center;
    private Matrix3x2 _turnOffset = Matrix3x2.Identity;

    internal ImageViewport(Size viewportSize)
    {
        SetViewportSize(viewportSize);
    }

    internal ViewportMode Mode { get; private set; } = ViewportMode.Fit;
    internal float Scale { get; private set; } = 1.0f;
    internal ImageOrientation Orientation { get; private set; }
    /// <summary>The image size as shown, after its orientation turns it.</summary>
    internal SizeF ImageSize => _imageSize;
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

    /// <param name="size">The stored size, before <paramref name="orientation"/> turns it.</param>
    internal void SetImageSize(Size size, ImageOrientation orientation = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Height);

        _storedImageSize = size;
        Orientation = orientation;
        _imageSize = orientation.Apply(_storedImageSize);
        Fit();
    }

    /// <summary>Turns the image while keeping the same part of it in the middle of the viewport.</summary>
    internal void SetOrientation(ImageOrientation orientation)
    {
        if (!HasImage)
        {
            return;
        }

        Matrix3x2.Invert(Orientation.GetTransform(_storedImageSize), out Matrix3x2 toStored);
        var storedCenter = Vector2.Transform(new Vector2(_center.X, _center.Y), toStored);
        var center = Vector2.Transform(storedCenter, orientation.GetTransform(_storedImageSize));
        Orientation = orientation;
        _imageSize = orientation.Apply(_storedImageSize);
        if (Mode == ViewportMode.Fit)
        {
            Fit();
            return;
        }

        Scale = Math.Clamp(Scale, GetMinimumScale(), MaximumScale);
        _center = ClampCenter(new PointF(center.X, center.Y), Scale);
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

    /// <summary>
    /// Shows the image moved by <paramref name="offset"/> on screen, while an animated turn
    /// catches up with an orientation that has already changed.
    /// </summary>
    internal void SetTurnOffset(Matrix3x2 offset) => _turnOffset = offset;

    /// <summary>Maps stored image pixels onto viewport pixels.</summary>
    internal Matrix3x2 ImageTransform
    {
        get
        {
            PointF location = GetDestinationRectangle().Location;
            return Orientation.GetTransform(_storedImageSize)
                * Matrix3x2.CreateScale(Scale)
                * Matrix3x2.CreateTranslation(location.X, location.Y)
                * _turnOffset;
        }
    }

    /// <summary>
    /// Maps the pixels of a stored-order copy of the image, of any resolution, onto viewport pixels.
    /// </summary>
    internal Matrix3x2 GetImageTransform(SizeF sourceSize) =>
        Matrix3x2.CreateScale(
            _storedImageSize.Width / sourceSize.Width,
            _storedImageSize.Height / sourceSize.Height)
        * ImageTransform;

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
