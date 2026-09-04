using System.Drawing;
using Dameview.Imaging;
using Dameview.Viewing;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class ImagePanel : IUiElement, IDisposable
{
    private readonly ID2D1DeviceContext _deviceContext;
    private readonly ImageViewport _viewport;
    private readonly ViewportAnimator _animator;
    private ID2D1Bitmap1? _image;
    private bool _isPanning;

    internal ImagePanel(
        ID2D1DeviceContext deviceContext,
        ImageViewport viewport,
        ViewportAnimator animator)
    {
        _deviceContext = deviceContext;
        _viewport = viewport;
        _animator = animator;
    }

    internal float ZoomPercentage => _viewport.Scale * 100.0f;

    internal unsafe void SetImage(DecodedImage image)
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

    public bool Update(in UiUpdateContext context)
    {
        return _animator.Update();
    }

    public void Draw(in UiDrawContext context, SizeF size)
    {
        if (_image is null)
        {
            return;
        }

        RectangleF destination = _viewport.GetDestinationRectangle();
        RectangleF destinationInDips = context.PixelsToDips(destination);

        context.RenderTarget.DrawBitmap(
            _image,
            new Rect(
                destinationInDips.X,
                destinationInDips.Y,
                destinationInDips.Width,
                destinationInDips.Height),
            1.0f,
            BitmapInterpolationMode.Linear,
            new Rect(0.0f, 0.0f, _image.PixelSize.Width, _image.PixelSize.Height));
    }

    public UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size)
    {
        switch (input.Kind)
        {
            case UiPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                _isPanning = true;
                _animator.BeginPan(input.Position.X, input.Position.Y);
                return new UiPointerResult(Consumed: true, CapturePointer: true);

            case UiPointerEventKind.Moved when _isPanning:
                _animator.PanTo(input.Position.X, input.Position.Y);
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
                _animator.ToggleFitAndActualSizeAt(input.Position.X, input.Position.Y);
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case UiPointerEventKind.Wheel:
                _animator.ZoomAt(input.Position.X, input.Position.Y, input.WheelDelta);
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            default:
                return default;
        }
    }

    public void Dispose()
    {
        _image?.Dispose();
    }
}
