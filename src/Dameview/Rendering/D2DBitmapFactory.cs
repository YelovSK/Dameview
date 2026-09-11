using System.Numerics;
using Dameview.Imaging;
using SharpGen.Runtime;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Dameview.Rendering;

internal static class D2DBitmapFactory
{
    private const float DefaultDpi = 96.0f;

    internal static unsafe ID2D1Bitmap1 Create(ID2D1DeviceContext deviceContext, DecodedImage image)
    {
        BitmapProperties1 properties = new(
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            DefaultDpi,
            DefaultDpi,
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

    internal static ID2D1Bitmap1 Create(
        ID2D1DeviceContext deviceContext,
        DecodedImageUpload image)
    {
        BitmapProperties1 properties = new(
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            DefaultDpi,
            DefaultDpi,
            BitmapOptions.None);

        return deviceContext.CreateBitmap(
            new SizeI(image.Width, image.Height),
            image.Pixels,
            (uint)image.Stride,
            properties);
    }

    internal static ID2D1Bitmap1 CreateScaled(
        ID2D1DeviceContext deviceContext,
        ID2D1Bitmap1 source,
        SizeI pixelSize,
        float dpi,
        float sharpness)
    {
        return CreateScaled(
            deviceContext,
            source,
            pixelSize,
            dpi,
            new Rect(
                0.0f,
                0.0f,
                PixelsToDips(pixelSize.Width, dpi),
                PixelsToDips(pixelSize.Height, dpi)),
            sharpness);
    }

    internal static ID2D1Bitmap1 CreateScaled(
        ID2D1DeviceContext deviceContext,
        ID2D1Bitmap1 source,
        SizeI pixelSize,
        float dpi,
        Rect destination,
        float sharpness)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize.Height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        ArgumentOutOfRangeException.ThrowIfLessThan(sharpness, 0.0f);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sharpness, 1.0f);

        using Scale scale = new(deviceContext)
        {
            Value = new Vector2(
                destination.Width / source.Size.Width,
                destination.Height / source.Size.Height),
            InterpolationMode = ScaleInterpolationMode.HighQualityCubic,
            BorderMode = BorderMode.Hard,
            Sharpness = sharpness,
        };
        scale.SetInput(0, source, true);
        using ID2D1Image output = scale.Output;

        return CreateTargetBitmap(deviceContext, pixelSize, dpi, () =>
        {
            deviceContext.DrawImage(
                output,
                new Vector2(destination.Left, destination.Top),
                null,
                InterpolationMode.Linear,
                CompositeMode.SourceOver);
        });
    }

    private static ID2D1Bitmap1 CreateTargetBitmap(
        ID2D1DeviceContext deviceContext,
        SizeI pixelSize,
        float dpi,
        Action draw)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize.Height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);

        BitmapProperties1 properties = new(
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            dpi,
            dpi,
            BitmapOptions.Target);
        ID2D1Bitmap1 bitmap = deviceContext.CreateBitmap(pixelSize, 0, 0, properties);
        try
        {
            deviceContext.Target = bitmap;
            deviceContext.SetDpi(dpi, dpi);
            deviceContext.BeginDraw();
            deviceContext.Clear(new Color4(0.0f, 0.0f, 0.0f, 0.0f));
            draw();
            deviceContext.EndDraw().CheckError();
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
        finally
        {
            deviceContext.Target = null;
        }
    }

    private static float PixelsToDips(float pixels, float dpi) => pixels * DefaultDpi / dpi;
}
