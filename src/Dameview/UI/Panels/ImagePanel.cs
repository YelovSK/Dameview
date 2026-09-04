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

    internal ImagePanel(ID2D1DeviceContext deviceContext, int width, int height)
    {
        _deviceContext = deviceContext;
        _viewport = new ImageViewport(width, height);
        _animator = new ViewportAnimator(_viewport);
    }

    internal bool HasImage => _image is not null;
    internal int ImageWidth => _image?.PixelSize.Width ?? 0;
    internal int ImageHeight => _image?.PixelSize.Height ?? 0;
    internal float ZoomPercentage => _viewport.Scale * 100.0f;

    internal unsafe void SetImage(DecodedImage image)
    {
        BitmapProperties1 properties = new(
            new PixelFormat(
                Format.B8G8R8A8_UNorm,
                Vortice.DCommon.AlphaMode.Premultiplied),
            96.0f,
            96.0f,
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
        _animator.Reset();
        _viewport.SetImageSize(image.Width, image.Height);
    }

    internal void SetViewportSize(int width, int height)
    {
        _animator.Reset();
        _viewport.SetViewportSize(width, height);
    }

    internal bool Update()
    {
        return _animator.Update();
    }

    internal bool Fit()
    {
        return _animator.Fit();
    }

    internal bool ShowActualSizeAt(float x, float y)
    {
        return _animator.ShowActualSizeAt(x, y);
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

    public bool HandlePointer(in UiPointerEvent input, SizeF size)
    {
        switch (input.Kind)
        {
            case UiPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                _isPanning = true;
                _animator.BeginPan(input.Position.X, input.Position.Y);
                return true;

            case UiPointerEventKind.Moved when _isPanning:
                _animator.PanTo(input.Position.X, input.Position.Y);
                return true;

            case UiPointerEventKind.Released when _isPanning:
                _isPanning = false;
                _animator.EndPan();
                return true;

            case UiPointerEventKind.DoubleClicked when input.Button == PointerButton.Primary:
                _isPanning = false;
                _animator.ToggleFitAndActualSizeAt(input.Position.X, input.Position.Y);
                return true;

            case UiPointerEventKind.Wheel:
                _animator.ZoomAt(input.Position.X, input.Position.Y, input.WheelDelta);
                return true;

            default:
                return false;
        }
    }

    public void Dispose()
    {
        _image?.Dispose();
    }
}
