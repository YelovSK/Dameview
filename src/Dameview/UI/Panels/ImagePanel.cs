using Dameview.Platform;
using System.Drawing;
using Dameview.Imaging;
using Dameview.Viewing;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class ImagePanel : UiElement, IDisposable
{
    private readonly ID2D1DeviceContext _deviceContext;
    private readonly ImageViewport _viewport;
    private readonly ViewportAnimator _animator;
    private readonly TimeProvider _timeProvider;
    private ID2D1Bitmap1? _image;
    private AnimatedImagePlayer? _imageAnimation;
    private bool _isPanning;
    private bool _isPreview;

    internal ImagePanel(
        ID2D1DeviceContext deviceContext,
        ImageViewport viewport,
        ViewportAnimator animator,
        TimeProvider? timeProvider = null)
    {
        _deviceContext = deviceContext;
        _viewport = viewport;
        _animator = animator;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal float ZoomPercentage => _viewport.Scale * 100.0f;
    internal TimeSpan? NextAnimationFrameDelay => _imageAnimation?.NextFrameDelay;
    internal Exception? AnimationError => _imageAnimation?.Error;

    internal unsafe void SetImage(DecodedImage image, bool isPreview)
    {
        _imageAnimation = null;
        SetBitmap(image);
        _isPreview = isPreview;
    }

    private unsafe void SetBitmap(DecodedImage image)
    {
        BitmapProperties1 properties = new(
            new PixelFormat(
                Format.B8G8R8A8_UNorm,
                Vortice.DCommon.AlphaMode.Premultiplied),
            UiDpi.Default,
            UiDpi.Default,
            BitmapOptions.None);

        ID2D1Bitmap1 newImage;
        fixed (byte* pixels = image.Pixels)
        {
            newImage = _deviceContext.CreateBitmap(
                new SizeI(image.Width, image.Height),
                (nint)pixels,
                (uint)image.Stride,
                properties);
        }

        _image?.Dispose();
        _image = newImage;
    }

    internal void SetAnimation(IAnimationSession animation)
    {
        _imageAnimation = null;
        SetBitmap(animation.FirstFrame.Image);
        _isPreview = false;
        _imageAnimation = new AnimatedImagePlayer(animation, _timeProvider);
    }

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        bool continues = _animator.Update(context.ElapsedSeconds);
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
        if (_image is null || _image.PixelSize.Width != image.Width || _image.PixelSize.Height != image.Height)
        {
            SetBitmap(image);
            return;
        }

        fixed (byte* pixels = image.Pixels)
        {
            _image.CopyFromMemory((nint)pixels, (uint)image.Stride);
        }
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        if (_image is null)
        {
            return;
        }

        RectangleF destination = _viewport.GetDestinationRectangle();
        if (_isPreview)
        {
            float scale = MathF.Min(Bounds.Width / _image.PixelSize.Width, Bounds.Height / _image.PixelSize.Height);
            float width = _image.PixelSize.Width * scale;
            float height = _image.PixelSize.Height * scale;
            destination = new RectangleF((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
        }
        else
        {
            destination = new RectangleF(
                context.PixelsToDips(destination.X),
                context.PixelsToDips(destination.Y),
                context.PixelsToDips(destination.Width),
                context.PixelsToDips(destination.Height));
        }

        context.RenderTarget.DrawBitmap(
            _image,
            new Rect(
                destination.X,
                destination.Y,
                destination.Width,
                destination.Height),
            context.Opacity,
            BitmapInterpolationMode.Linear,
            new Rect(0.0f, 0.0f, _image.PixelSize.Width, _image.PixelSize.Height));
    }

    internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
    {
        if (_isPreview)
        {
            return new UiPointerResult(Consumed: true);
        }

        switch (input.Kind)
        {
            case UiPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                _isPanning = true;
                _animator.BeginPan(ToPixels(input.Position.X), ToPixels(input.Position.Y));
                return new UiPointerResult(Consumed: true, CapturePointer: true);

            case UiPointerEventKind.Moved when _isPanning:
                _animator.PanTo(ToPixels(input.Position.X), ToPixels(input.Position.Y));
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case UiPointerEventKind.Released when _isPanning:
                _isPanning = false;
                _animator.EndPan();
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case UiPointerEventKind.Cancelled when _isPanning:
                _isPanning = false;
                _animator.Reset();
                return new UiPointerResult(Consumed: true);

            case UiPointerEventKind.DoubleClicked when input.Button == PointerButton.Primary:
                _isPanning = false;
                _animator.ToggleFitAndActualSizeAt(ToPixels(input.Position.X), ToPixels(input.Position.Y));
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case UiPointerEventKind.Wheel:
                _animator.ZoomAt(ToPixels(input.Position.X), ToPixels(input.Position.Y), input.WheelDelta);
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            default:
                return default;
        }
    }

    public void Dispose()
    {
        _imageAnimation = null;
        _image?.Dispose();
    }

    private float ToPixels(float value) => Root?.DipsToPixels(value) ?? value;
}
