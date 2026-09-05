using Dameview.Imaging;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Dameview.UI;

internal static class D2DBitmapFactory
{
    internal static unsafe ID2D1Bitmap1 Create(ID2D1DeviceContext deviceContext, DecodedImage image)
    {
        BitmapProperties1 properties = new(
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            UiDpi.Default,
            UiDpi.Default,
            BitmapOptions.None);

        fixed (byte* pixels = image.Pixels)
        {
            return deviceContext.CreateBitmap(
                new SizeI(image.Width, image.Height),
                (nint)pixels,
                (uint)image.Stride,
                properties);
        }
    }
}
