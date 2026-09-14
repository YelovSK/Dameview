using System.Drawing;
using Dameview.Imaging;
using Dameview.Platform;
using Dameview.Rendering;
using Dameview.UI.Animation;
using Dameview.Viewing;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class ImagePanel : UiElement, IDisposable
{
    private readonly ID2D1DeviceContext _deviceContext;
    private readonly ID2D1DeviceContext _scaleContext;
    private readonly ImagePresentationCache _presentationCache;
    private ImageViewport _viewport;
    private ViewportAnimator _animator;
    private readonly TimeProvider _timeProvider;
    private const float PanStartThresholdDips = 4.0f;
    private ID2D1Bitmap1? _ownedImage;
    private ID2D1Bitmap1? _previewImage;
    private ID2D1Bitmap1? _cachedImage;
    private TiledImageRenderer? _tiledImage;
    private AnimatedImagePlayer? _imageAnimation;
    private bool _isPanning;
    private bool _pointerPressed;
    private PointF _panStart;
    private bool _isPreview;
    private readonly AnimatedFloat _previewFade = new(0.0f, 20.0);
    private const float PreviewBlurStandardDeviation = 0.75f;
    private System.Drawing.Size _viewportPixelSize;

    internal ImagePanel(
        ID2D1DeviceContext deviceContext,
        ImageViewport viewport,
        ViewportAnimator animator,
        TimeProvider? timeProvider = null)
    {
        _deviceContext = deviceContext;
        using ID2D1Device device = deviceContext.Device;
        _scaleContext = device.CreateDeviceContext();
        _presentationCache = new ImagePresentationCache(deviceContext);
        _viewport = viewport;
        _animator = animator;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal float ZoomPercentage => _viewport.Scale * 100.0f;
    internal TimeSpan? NextAnimationFrameDelay => _imageAnimation?.NextFrameDelay;
    internal Exception? AnimationError => _imageAnimation?.Error;

    internal void Bind(
        ImageViewport viewport,
        ViewportAnimator animator)
    {
        _imageAnimation = null;
        _presentationCache.Clear();
        _cachedImage = null;
        _tiledImage?.Dispose();
        _tiledImage = null;
        _ownedImage?.Dispose();
        _ownedImage = null;
        _previewImage?.Dispose();
        _previewImage = null;
        _previewFade.SetValue(0.0f);
        _isPreview = false;
        _pointerPressed = false;
        _isPanning = false;
        _viewport = viewport;
        _animator = animator;
        _animator.Reset();
        _viewport.SetViewportSize(_viewportPixelSize.Width, _viewportPixelSize.Height);
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

        switch (image)
        {
            case CachedBitmapRepresentation cached:
                SetCachedImage(cached.Bitmap);
                break;

            case DecodedImageRepresentation decoded:
                SetDecodedImage(decoded.Image, isPreview);
                break;

            case TiledImageRepresentation tiled:
                SetTiledImage(tiled.Source);
                break;

            case AnimatedImageRepresentation animated:
                SetAnimation(animated.Animation);
                break;

            default:
                throw new NotSupportedException($"Unsupported image representation: {image.GetType().Name}");
        }
    }

    private unsafe void SetDecodedImage(DecodedImage image, bool isPreview)
    {
        _imageAnimation = null;
        _presentationCache.Clear();
        ClearCachedImage();
        _tiledImage?.Dispose();
        _tiledImage = null;
        SetBitmap(image);
        _isPreview = isPreview;
        if (isPreview && _ownedImage is { } previewSource)
        {
            CreatePreviewImage(previewSource);
        }
    }

    private void SetTiledImage(IImageTileSource source)
    {
        _imageAnimation = null;
        _presentationCache.Clear();
        ClearCachedImage();
        _ownedImage?.Dispose();
        _ownedImage = null;
        _tiledImage?.Dispose();
        _tiledImage = new TiledImageRenderer(
            _deviceContext,
            source,
            _viewport,
            InvalidateVisual);
        _isPreview = false;
    }

    private void SetBitmap(DecodedImage image)
    {
        ID2D1Bitmap1 newImage = D2DBitmapFactory.Create(_deviceContext, image);

        _ownedImage?.Dispose();
        _ownedImage = newImage;
    }

    private void SetCachedImage(ID2D1Bitmap1 image)
    {
        _imageAnimation = null;
        _presentationCache.Clear();
        _ownedImage?.Dispose();
        _ownedImage = null;
        _tiledImage?.Dispose();
        _tiledImage = null;
        _cachedImage = image;
        _isPreview = false;
    }

    private void ClearCachedImage()
    {
        _cachedImage = null;
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
        _presentationCache.Clear();
        _animator.Reset();
        _viewport.SetViewportSize(pixelSize.Width, pixelSize.Height);
    }

    private void SetAnimation(IAnimationSession animation)
    {
        _imageAnimation = null;
        _presentationCache.Clear();
        ClearCachedImage();
        _tiledImage?.Dispose();
        _tiledImage = null;
        SetBitmap(animation.FirstFrame.Image);
        _isPreview = false;
        _imageAnimation = new AnimatedImagePlayer(animation, _timeProvider);
    }

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        bool continues = _animator.Update(context.ElapsedSeconds, context.AnimationsEnabled);
        continues |= _previewFade.Update(context);
        if (!_isPreview && _previewFade.Current == 0.0f && _previewImage is not null)
        {
            _previewImage.Dispose();
            _previewImage = null;
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
        if (_tiledImage is { } tiledImage)
        {
            tiledImage.Draw(context, ToPixels(Bounds.Width), ToPixels(Bounds.Height));
            DrawPreviewTransition(context);
            return;
        }

        ID2D1Bitmap1? image = _cachedImage ?? _ownedImage;
        if (image is null)
        {
            return;
        }

        RectangleF destinationPixels = _viewport.GetDestinationRectangle();
        if (_isPreview && _previewImage is { } preview)
        {
            DrawBitmap(context, preview, destinationPixels);
            return;
        }

        if (_imageAnimation is not null
            || _animator.IsAnimating
            || _isPanning
            || _viewport.Scale == 1.0f)
        {
            _presentationCache.Clear();
            DrawBitmap(context, image, destinationPixels);
            DrawPreviewTransition(context);
            return;
        }

        ImagePresentation? presentation = _presentationCache.GetOrCreate(
            image,
            destinationPixels,
            _viewportPixelSize,
            context.Dpi);
        if (presentation is not { } cached)
        {
            return;
        }

        context.RenderTarget.DrawBitmap(
            cached.Bitmap,
            new Rect(
                context.PixelsToDips(cached.OffsetPixels.X),
                context.PixelsToDips(cached.OffsetPixels.Y),
                cached.Bitmap.Size.Width,
                cached.Bitmap.Size.Height),
            context.Opacity,
            BitmapInterpolationMode.Linear,
            new Rect(0.0f, 0.0f, cached.Bitmap.Size.Width, cached.Bitmap.Size.Height));
        DrawPreviewTransition(context);
    }

    private void DrawPreviewTransition(in UiDrawContext context)
    {
        if (_previewImage is { } preview && _previewFade.Current > 0.0f)
        {
            DrawBitmap(context, preview, _viewport.GetDestinationRectangle(), _previewFade.Current);
        }
    }

    private void DrawBitmap(
        in UiDrawContext context,
        ID2D1Bitmap1 image,
        RectangleF destinationPixels,
        float opacity = 1.0f)
    {
        _deviceContext.DrawBitmap(
            image,
            new Rect(
                context.PixelsToDips(destinationPixels.X),
                context.PixelsToDips(destinationPixels.Y),
                context.PixelsToDips(destinationPixels.Width),
                context.PixelsToDips(destinationPixels.Height)),
            context.Opacity * opacity,
            InterpolationMode.Linear,
            new Rect(0.0f, 0.0f, image.PixelSize.Width, image.PixelSize.Height),
            null);
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
                _panStart = new PointF(
                    ToPixels(input.Position.X),
                    ToPixels(input.Position.Y));
                return new UiPointerResult(Consumed: true, CapturePointer: true);

            case WindowPointerEventKind.Moved when _pointerPressed:
                PointF pointer = new(
                    ToPixels(input.Position.X),
                    ToPixels(input.Position.Y));
                if (!_isPanning)
                {
                    float threshold = ToPixels(PanStartThresholdDips);
                    float distanceX = pointer.X - _panStart.X;
                    float distanceY = pointer.Y - _panStart.Y;
                    if ((distanceX * distanceX) + (distanceY * distanceY) < threshold * threshold)
                    {
                        return new UiPointerResult(Consumed: true);
                    }

                    _isPanning = true;
                    _animator.BeginPan(_panStart.X, _panStart.Y);
                }

                _animator.PanTo(pointer.X, pointer.Y);
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Released when _pointerPressed:
                _pointerPressed = false;
                if (!_isPanning)
                {
                    return new UiPointerResult(Consumed: true);
                }

                _isPanning = false;
                _animator.EndPan();
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Cancelled when _pointerPressed:
                _pointerPressed = false;
                _isPanning = false;
                _animator.Reset();
                return new UiPointerResult(Consumed: true);

            case WindowPointerEventKind.DoubleClicked when input.Button == PointerButton.Primary:
                _pointerPressed = false;
                _isPanning = false;
                _animator.ToggleFitAndActualSizeAt(ToPixels(input.Position.X), ToPixels(input.Position.Y));
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case WindowPointerEventKind.Wheel:
                _animator.ZoomAt(ToPixels(input.Position.X), ToPixels(input.Position.Y), input.WheelDelta);
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            default:
                return default;
        }
    }

    public void Dispose()
    {
        _imageAnimation = null;
        _presentationCache.Dispose();
        ClearCachedImage();
        _tiledImage?.Dispose();
        _ownedImage?.Dispose();
        _previewImage?.Dispose();
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
}
