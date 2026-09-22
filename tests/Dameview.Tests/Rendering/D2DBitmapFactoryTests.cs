using Dameview.Imaging;
using Dameview.Rendering;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Dameview.Tests.Rendering;

[TestClass]
public sealed class D2DBitmapFactoryTests
{
    // Direct2D may leave padding between mapped rows, and whether it does depends on the size.
    // These cover awkward widths as well as round ones, because a reader that assumes rows are
    // packed draws a skewed, repeating pattern instead of the image.
    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(37, 11)]
    [DataRow(255, 255)]
    [DataRow(256, 256)]
    [DataRow(1023, 17)]
    [DataRow(1920, 1080)]
    public void ReadBackReturnsTheUploadedPixels(int width, int height)
    {
        using var device = new TestDevice();
        DecodedImage source = CreateImage(width, height);

        using ID2D1Bitmap1 bitmap = D2DBitmapFactory.Create(device.Context, source);
        DecodedImage readBack = D2DBitmapFactory.ReadBack(device.Context, bitmap);

        Assert.AreEqual(source.Width, readBack.Width);
        Assert.AreEqual(source.Height, readBack.Height);
        AssertSameRows(source, readBack);
    }

    [TestMethod]
    [DataRow(37, 11)]
    [DataRow(1023, 17)]
    [DataRow(1920, 1080)]
    public void ReadBackSurvivesAnUploadToAnotherDevice(int width, int height)
    {
        using var first = new TestDevice();
        using var second = new TestDevice();
        DecodedImage source = CreateImage(width, height);

        using ID2D1Bitmap1 original = D2DBitmapFactory.Create(first.Context, source);
        DecodedImage moved = D2DBitmapFactory.ReadBack(first.Context, original);
        using ID2D1Bitmap1 uploaded = D2DBitmapFactory.Create(second.Context, moved);
        DecodedImage arrived = D2DBitmapFactory.ReadBack(second.Context, uploaded);

        AssertSameRows(source, arrived);
    }

    private static DecodedImage CreateImage(int width, int height)
    {
        int stride = width * 4;
        var pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i++)
        {
            // Premultiplied alpha: keep the colour channels at or below the alpha channel.
            pixels[i] = (i % 4) == 3 ? (byte)255 : (byte)(i % 251);
        }

        return new DecodedImage(width, height, stride, pixels);
    }

    private static void AssertSameRows(DecodedImage expected, DecodedImage actual)
    {
        int rowBytes = expected.Width * 4;
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < rowBytes; x++)
            {
                Assert.AreEqual(
                    expected.Pixels[(y * expected.Stride) + x],
                    actual.Pixels[(y * actual.Stride) + x],
                    $"row {y}, byte {x}");
            }
        }
    }

    private sealed class TestDevice : IDisposable
    {
        private readonly ID3D11Device _d3dDevice;
        private readonly IDXGIDevice _dxgiDevice;
        private readonly ID2D1Factory1 _factory;
        private readonly ID2D1Device _device;

        internal TestDevice()
        {
            _d3dDevice = D2DRenderer.CreateDevice(DriverType.Warp);
            _dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice>();
            _factory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
            _device = _factory.CreateDevice(_dxgiDevice);
            Context = _device.CreateDeviceContext();
        }

        internal ID2D1DeviceContext Context { get; }

        public void Dispose()
        {
            Context.Dispose();
            _device.Dispose();
            _factory.Dispose();
            _dxgiDevice.Dispose();
            _d3dDevice.Dispose();
        }
    }
}
