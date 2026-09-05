using System.Drawing;
using Dameview.UI;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using Vortice.WIC;
using static Vortice.Direct2D1.D2D1;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class UiDrawContextTests
{
    [TestMethod]
    public void ReusedBrushPreservesEarlierDrawsAndDoesNotLeakOpacityOrColor()
    {
        using var wic = new IWICImagingFactory2();
        using IWICBitmap bitmap = wic.CreateBitmap(40, 10,
            Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapCreateCacheOption.CacheOnLoad);
        using ID2D1Factory factory = D2D1CreateFactory<ID2D1Factory>();
        using ID2D1RenderTarget target = factory.CreateWicBitmapRenderTarget(bitmap, new RenderTargetProperties());
        target.SetDpi(96, 96);
        using ID2D1SolidColorBrush brush = target.CreateSolidColorBrush(default(Color4));
        var context = new UiDrawContext(target, brush, UiTheme.Default, 96);
        var red = new Color4(1, 0, 0, 1);
        var green = new Color4(0, 1, 0, 1);
        var blue = new Color4(0, 0, 1, 0.5f);

        target.BeginDraw();
        target.Clear(default(Color4));
        context.WithOpacity(0.5f).FillRoundedRectangle(Block(0), red, opacity: 0.5f);
        context.FillRoundedRectangle(Block(10), green);
        context.WithOpacity(0.5f).WithOpacity(0.5f).FillRoundedRectangle(Block(20), blue);
        context.FillRoundedRectangle(Block(30), red);
        target.EndDraw().CheckError();

        byte[] pixels = new byte[40 * 10 * 4];
        bitmap.CopyPixels(40 * 4, pixels);
        AssertPixel(pixels, 5, 0, 0, 64, 64);
        AssertPixel(pixels, 15, 0, 255, 0, 255);
        AssertPixel(pixels, 25, 32, 0, 0, 32);
        AssertPixel(pixels, 35, 0, 0, 255, 255);
    }

    [TestMethod]
    public void ExistingElementUsesThePaletteOfEachFrame()
    {
        using var wic = new IWICImagingFactory2();
        using IWICBitmap bitmap = wic.CreateBitmap(40, 10,
            Vortice.WIC.PixelFormat.Format32bppPBGRA, BitmapCreateCacheOption.CacheOnLoad);
        using ID2D1Factory factory = D2D1CreateFactory<ID2D1Factory>();
        using ID2D1RenderTarget target = factory.CreateWicBitmapRenderTarget(bitmap, new RenderTargetProperties());
        target.SetDpi(96, 96);
        using ID2D1SolidColorBrush brush = target.CreateSolidColorBrush(default(Color4));
        var element = new PaletteElement();
        var red = UiTheme.Default with { PrimaryText = new Color4(1, 0, 0, 1) };
        var green = UiTheme.Default with { PrimaryText = new Color4(0, 1, 0, 1) };
        foreach (UiTheme palette in new[] { red, green })
        {
            var context = new UiDrawContext(target, brush, palette, 96);
            target.BeginDraw();
            target.Clear(default(Color4));
            context.DrawElement(element, new RectangleF(0, 0, 10, 10));
            target.EndDraw().CheckError();
            byte[] pixels = new byte[40 * 10 * 4];
            bitmap.CopyPixels(40 * 4, pixels);
            AssertPixel(pixels, 5, 0, palette == green ? 255 : 0, palette == red ? 255 : 0, 255);
        }
    }

    private static RoundedRectangle Block(int x)
    {
        return new RoundedRectangle(new RectangleF(x, 0, 10, 10), 0, 0);
    }

    private static void AssertPixel(byte[] pixels, int x, int blue, int green, int red, int alpha)
    {
        int offset = ((5 * 40) + x) * 4;
        int[] expected = [blue, green, red, alpha];
        for (int channel = 0; channel < 4; channel++)
        {
            Assert.IsTrue(Math.Abs(pixels[offset + channel] - expected[channel]) <= 1,
                $"Pixel {x}, channel {channel}: expected {expected[channel]}, got {pixels[offset + channel]}.");
        }
    }

    private sealed class PaletteElement : IUiElement
    {
        public void Draw(in UiDrawContext context, SizeF size)
        {
            context.FillRoundedRectangle(Block(0), context.Palette.PrimaryText);
        }

        public UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size)
        {
            return default;
        }
    }
}
