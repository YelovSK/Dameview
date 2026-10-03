using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using Dameview.Imaging;
using Dameview.Imaging.Animation;
using Dameview.Imaging.Loading;
using Dameview.Rendering;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;
using Dameview.UI.Presentation;
using Dameview.Viewing;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class ImagePanel : UiElement, IDisposable
{
    private ID2D1DeviceContext _deviceContext;
    private ID2D1DeviceContext _scaleContext;
    private readonly ImagePresentationCache _presentationCache;
    private readonly Action<PointF> _contextMenuRequested;
    private ImageViewport _viewport;
    private ViewportAnimator _animator;
    private const float PanStartThresholdDips = 4.0f;
    // Long enough to ride out the gaps between pointer moves while dragging.
    private static readonly long SettleTicks = Stopwatch.Frequency / 50;
    // Borrowed from the session, which disposes it after replacing it or clearing the panel.
    private ImageRepresentation? _image;
    private ID2D1Bitmap1? _ownedImage;
    private ID2D1Bitmap1? _previewImage;
    private ID2D1Bitmap1? _cachedImage;
    private TiledImageRenderer? _tiledImage;
    private AnimatedImagePlayer? _imageAnimation;
    private ID2D1Bitmap1? _placedImage;
    private Matrix3x2 _placement;
    private long _settlesAt;
    private bool _pointerPressed;
    private PointF _panFrom;
    private bool _isPreview;
    private readonly AnimatedFloat _previewFade = new(0.0f, 20.0);
    private const float PreviewBlurStandardDeviation = 0.75f;
    private System.Drawing.Size _viewportPixelSize;

    internal ImagePanel(
        ID2D1DeviceContext deviceContext,
        ImageViewport viewport,
        ViewportAnimator animator,
        Action<PointF> contextMenuRequested)
    {
        _contextMenuRequested = contextMenuRequested;
        _deviceContext = deviceContext;
        _scaleContext = D2DBitmapFactory.CreateOffscreenContext(deviceContext);
        _presentationCache = new ImagePresentationCache(deviceContext);
        _viewport = viewport;
        _animator = animator;
        _animator.Started += InvalidateVisual;
    }

    /// <summary>Whether zooming past 100% shows the image's pixels as squares instead of smoothing them.</summary>
    internal bool SharpPixels
    {
        get;
        set
        {
            field = value;
            InvalidateVisual();
        }
    }

    internal float ZoomPercentage => _viewport.Scale * 100.0f;
    internal SizeF ImageSize => _viewport.ImageSize;
    internal TimeSpan? NextAnimationFrameDelay => _imageAnimation?.NextFrameDelay ?? SettleDelay;
    internal Exception? AnimationError => _imageAnimation?.Error;

    internal void RecreateDeviceResources(ID2D1DeviceContext deviceContext)
    {
        ImageRepresentation? image = _image;
        bool isPreview = _isPreview;
        ClearImage();
        _deviceContext = deviceContext;
        _scaleContext.Dispose();
        _scaleContext = D2DBitmapFactory.CreateOffscreenContext(deviceContext);
        _presentationCache.Recreate(deviceContext);
        if (image is not null)
        {
            SetImage(image, isPreview);
        }
    }

    internal void ClearImage()
    {
        ReleaseContent();
        ClearPreviewTransition();
        _isPreview = false;
    }

    // Drops whatever the current image is drawn from. The preview fade is handled separately.
    private void ReleaseContent()
    {
        _image = null;
        _imageAnimation?.Pause();
        _imageAnimation = null;
        _presentationCache.Clear();
        _cachedImage = null;
        _tiledImage?.Dispose();
        _tiledImage = null;
        _ownedImage?.Dispose();
        _ownedImage = null;
    }

    internal void Bind(
        ImageViewport viewport,
        ViewportAnimator animator)
    {
        ClearImage();
        _pointerPressed = false;
        _viewport = viewport;
        _animator.Started -= InvalidateVisual;
        _animator = animator;
        _animator.Started += InvalidateVisual;
        _animator.SetViewportSize(_viewportPixelSize);
    }

    internal void SetImage(ImageRepresentation image, bool isPreview)
    {
        if (isPreview)
        {
            ClearPreviewTransition();
        }
        else
        {
            BeginPreviewTransition();
        }

        ReleaseContent();
        _image = image;
        _isPreview = isPreview;
        switch (image)
        {
            case CachedBitmapLease cached:
                _cachedImage = cached.Bitmap;
                if (isPreview)
                {
                    CreatePreviewImage(cached.Bitmap);
                }

                break;

            case TiledImageRepresentation tiled:
                _tiledImage = new TiledImageRenderer(_deviceContext, tiled.Source, _viewport, InvalidateVisual);
                break;

            case AnimatedImageRepresentation animated:
                SetBitmap(animated.Player.CurrentImage);
                _imageAnimation = animated.Player;
                break;

            default:
                throw new NotSupportedException($"Unsupported image representation: {image.GetType().Name}");
        }
    }

    private void SetBitmap(DecodedImage image)
    {
        ID2D1Bitmap1 newImage = D2DBitmapFactory.Create(_deviceContext, image);

        _ownedImage?.Dispose();
        _ownedImage = newImage;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        var pixelSize = new System.Drawing.Size(
            Math.Max(0, (int)MathF.Round(ToPixels(finalSize.Width))),
            Math.Max(0, (int)MathF.Round(ToPixels(finalSize.Height))));
        if (pixelSize == _viewportPixelSize)
        {
            return;
        }

        _viewportPixelSize = pixelSize;
        _animator.SetViewportSize(pixelSize);
    }

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        bool continues = _animator.Update(context.ElapsedSeconds, context.AnimationsEnabled);
        continues |= _previewFade.Update(context);
        if (!_isPreview && _previewFade.Current == 0.0f && _previewImage is not null)
        {
            ClearPreviewTransition();
        }

        if (_imageAnimation is { } animation)
        {
            animation.Update();
            if (animation.TryTakeUpdatedImage(out DecodedImage image))
            {
                UpdateImagePixels(image);
            }
        }

        return continues;
    }

    private unsafe void UpdateImagePixels(DecodedImage image)
    {
        if (_ownedImage is null
            || _ownedImage.PixelSize.Width != image.Width
            || _ownedImage.PixelSize.Height != image.Height)
        {
            SetBitmap(image);
            return;
        }

        fixed (byte* pixels = image.Pixels)
        {
            _ownedImage.CopyFromMemory((nint)pixels, (uint)image.Stride);
        }
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        DrawContent(context);
        DrawPreviewTransition(context);
    }

    private void DrawContent(in UiDrawContext context)
    {
        if (_tiledImage is { } tiledImage)
        {
            tiledImage.Draw(context, ToPixels(Bounds.Width), ToPixels(Bounds.Height), Interpolation);
            return;
        }

        ID2D1Bitmap1? image = _cachedImage ?? _ownedImage;
        if (image is null)
        {
            return;
        }

        if (_isPreview && _previewImage is { } preview)
        {
            DrawPreview(context, preview, _viewport.GetDestinationRectangle());
            return;
        }

        Matrix3x2 placement = _viewport.GetImageTransform(new SizeF(image.PixelSize.Width, image.PixelSize.Height));

        // Building the rescale is expensive, drawing one we already built is not. So while things
        // move, reuse a matching one if we have it and fall back to a plain draw if we don't.
        bool settling = IsSettling(image, placement);
        if (Interpolation == BitmapInterpolationMode.NearestNeighbor)
        {
            // The resampled copy would smooth away exactly the pixels this is meant to show.
            _presentationCache.Clear();
            DrawImage(context, image, placement, Interpolation);
            return;
        }

        ImagePresentation? presentation = settling
            || _imageAnimation is not null
            || Root?.IsResizing == true
            || _viewport.Scale == 1.0f
                ? _presentationCache.TryGet(image, placement, _viewportPixelSize, context.Dpi)
                : _presentationCache.GetOrCreate(image, placement, _viewportPixelSize, context.Dpi);

        if (presentation is not { } cached)
        {
            _presentationCache.Clear();
            DrawImage(context, image, placement);
            return;
        }

        context.DrawBitmap(
            cached.Bitmap,
            new Rect(
                context.PixelsToDips(cached.OffsetPixels.X),
                context.PixelsToDips(cached.OffsetPixels.Y),
                cached.Bitmap.Size.Width,
                cached.Bitmap.Size.Height));
    }

    private BitmapInterpolationMode Interpolation => SharpPixels && _viewport.Scale > 1.0f
        ? BitmapInterpolationMode.NearestNeighbor
        : BitmapInterpolationMode.Linear;

    /// <summary>How long until a moved image is drawn sharply, if it is waiting to be.</summary>
    private TimeSpan? SettleDelay
    {
        get
        {
            long now = Stopwatch.GetTimestamp();
            return now < _settlesAt ? Stopwatch.GetElapsedTime(now, _settlesAt) : null;
        }
    }

    /// <summary>
    /// Whether the image moved too recently to be worth drawing sharply, whatever moved it.
    /// A newly shown image has not moved.
    /// </summary>
    private bool IsSettling(ID2D1Bitmap1 image, Matrix3x2 placement)
    {
        long now = Stopwatch.GetTimestamp();
        if (!ReferenceEquals(image, _placedImage))
        {
            _placedImage = image;
            _settlesAt = now;
        }
        else if (placement != _placement)
        {
            _settlesAt = now + SettleTicks;
        }

        _placement = placement;
        return now < _settlesAt;
    }

    private void DrawPreviewTransition(in UiDrawContext context)
    {
        if (_previewImage is { } preview && _previewFade.Current > 0.0f)
        {
            DrawPreview(context, preview, _viewport.GetDestinationRectangle(), _previewFade.Current);
        }
    }

    /// <param name="placement">Maps the image's pixels onto viewport pixels.</param>
    private static void DrawImage(
        in UiDrawContext context,
        ID2D1Bitmap1 image,
        Matrix3x2 placement,
        BitmapInterpolationMode interpolation = BitmapInterpolationMode.Linear)
    {
        using TransformScope scope = context.PushTransform(
            placement * Matrix3x2.CreateScale(context.PixelsToDips(1.0f)));
        var bounds = new Rect(0.0f, 0.0f, image.PixelSize.Width, image.PixelSize.Height);
        context.DrawBitmap(image, bounds, bounds, interpolation);
    }

    // Thumbnails come already turned upright, so previews skip the image's orientation.
    private static void DrawPreview(
        in UiDrawContext context,
        ID2D1Bitmap1 image,
        RectangleF destinationPixels,
        float opacity = 1.0f)
    {
        context.DrawBitmap(
            image,
            new Rect(
                context.PixelsToDips(destinationPixels.X),
                context.PixelsToDips(destinationPixels.Y),
                context.PixelsToDips(destinationPixels.Width),
                context.PixelsToDips(destinationPixels.Height)),
            opacity);
    }

    internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
    {
        if (_isPreview)
        {
            return new UiPointerResult(Consumed: true);
        }

        switch (input.Kind)
        {
            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                _pointerPressed = true;
                _panFrom = ToPixels(input.Position);
                return new UiPointerResult(Consumed: true, CapturePointer: true);

            case WindowPointerEventKind.Moved when _pointerPressed:
                PointF pointer = ToPixels(input.Position);
                if (!_animator.IsPanning)
                {
                    float threshold = ToPixels(PanStartThresholdDips);
                    float distanceX = pointer.X - _panFrom.X;
                    float distanceY = pointer.Y - _panFrom.Y;
                    if ((distanceX * distanceX) + (distanceY * distanceY) < threshold * threshold)
                    {
                        return new UiPointerResult(Consumed: true);
                    }

                    _animator.BeginPan(_panFrom);
                }

                _animator.PanTo(pointer);
                _panFrom = pointer;
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Released when _pointerPressed:
                _pointerPressed = false;
                if (!_animator.IsPanning)
                {
                    return new UiPointerResult(Consumed: true);
                }

                _animator.EndPan();
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Cancelled when _pointerPressed:
                _pointerPressed = false;
                _animator.Reset();
                return new UiPointerResult(Consumed: true);

            case WindowPointerEventKind.DoubleClicked when input.Button == PointerButton.Primary:
                _pointerPressed = false;
                _animator.ToggleFitAndActualSizeAt(ToPixels(input.Position));
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Secondary:
                _contextMenuRequested(input.Position);
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Wheel:
                _animator.ZoomAt(ToPixels(input.Position), input.WheelDelta);
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            default:
                return default;
        }
    }

    public void Dispose()
    {
        _animator.Started -= InvalidateVisual;
        ClearImage();
        _presentationCache.Dispose();
        _scaleContext.Dispose();
    }

    private void CreatePreviewImage(ID2D1Bitmap1 source)
    {
        try
        {
            _previewImage = D2DBitmapFactory.CreateBlurred(
                _scaleContext,
                source,
                PreviewBlurStandardDeviation);
        }
        catch
        {
            // The blurred preview is best-effort; fall back to drawing the sharp thumbnail.
            _previewImage = null;
        }
    }

    private void BeginPreviewTransition()
    {
        if (!_isPreview || _previewImage is null)
        {
            return;
        }

        _previewFade.SetValue(1.0f);
        _previewFade.SetTarget(0.0f);
        InvalidateVisual();
    }

    private void ClearPreviewTransition()
    {
        _previewImage?.Dispose();
        _previewImage = null;
        _previewFade.SetValue(0.0f);
    }

    private float ToPixels(float value) => Root?.DipsToPixels(value) ?? value;

    private PointF ToPixels(PointF point) => new(ToPixels(point.X), ToPixels(point.Y));
}
