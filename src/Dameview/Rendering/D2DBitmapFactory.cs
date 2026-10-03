using System.Numerics;
using System.Runtime.InteropServices;
using Dameview.Imaging;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct2D1.Effects;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Dameview.Rendering;

internal static class D2DBitmapFactory
{
    private const float DefaultDpi = 96.0f;

    /// <summary>
    /// A context on the same device for building bitmaps while <paramref name="deviceContext"/>
    /// is in the middle of drawing a frame.
    /// </summary>
    internal static ID2D1DeviceContext CreateOffscreenContext(ID2D1DeviceContext deviceContext)
    {
        using ID2D1Device device = deviceContext.Device;
        return device.CreateDeviceContext();
    }

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

    /// <summary>Copies a bitmap to system memory, so it can move to another device.</summary>
    internal static DecodedImage ReadBack(ID2D1DeviceContext deviceContext, ID2D1Bitmap1 bitmap)
    {
        SizeI pixelSize = bitmap.PixelSize;
        BitmapProperties1 properties = new(
            bitmap.PixelFormat,
            DefaultDpi,
            DefaultDpi,
            BitmapOptions.CpuRead | BitmapOptions.CannotDraw);
        using ID2D1Bitmap1 readable = deviceContext.CreateBitmap(pixelSize, nint.Zero, 0, properties);
        readable.CopyFromBitmap(bitmap);

        MappedRectangle mapped = readable.Map(MapOptions.Read);
        try
        {
            int stride = checked((int)mapped.Pitch);
            byte[] pixels = new byte[(long)stride * pixelSize.Height];
            Marshal.Copy(mapped.Bits, pixels, 0, pixels.Length);
            return new DecodedImage(pixelSize.Width, pixelSize.Height, stride, pixels);
        }
        finally
        {
            readable.Unmap();
        }
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
            Matrix3x2.CreateScale(
                PixelsToDips(pixelSize.Width, dpi) / source.Size.Width,
                PixelsToDips(pixelSize.Height, dpi) / source.Size.Height),
            sharpness);
    }

    /// <param name="placement">
    /// Maps source pixels onto the new bitmap's DIPs. Besides scaling and moving, it may turn by
    /// quarter turns and mirror.
    /// </param>
    internal static ID2D1Bitmap1 CreateScaled(
        ID2D1DeviceContext deviceContext,
        ID2D1Bitmap1 source,
        SizeI pixelSize,
        float dpi,
        Matrix3x2 placement,
        float sharpness)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize.Height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        ArgumentOutOfRangeException.ThrowIfLessThan(sharpness, 0.0f);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sharpness, 1.0f);

        // The scale effect resamples in high quality, so it does all the scaling, and the
        // transform it is drawn with only turns and moves.
        var factor = new Vector2(
            new Vector2(placement.M11, placement.M12).Length(),
            new Vector2(placement.M21, placement.M22).Length());
        using Scale scale = new(deviceContext)
        {
            Value = factor,
            InterpolationMode = ScaleInterpolationMode.HighQualityCubic,
            BorderMode = BorderMode.Hard,
            Sharpness = sharpness,
        };
        scale.SetInput(0, source, true);
        using ID2D1Image output = scale.Output;

        return CreateTargetBitmap(deviceContext, pixelSize, dpi, () =>
        {
            deviceContext.Transform = Matrix3x2.CreateScale(Vector2.One / factor) * placement;
            try
            {
                deviceContext.DrawImage(
                    output,
                    Vector2.Zero,
                    null,
                    InterpolationMode.Linear,
                    CompositeMode.SourceOver);
            }
            finally
            {
                deviceContext.Transform = Matrix3x2.Identity;
            }
        });
    }

    internal static ID2D1Bitmap1 CreateBlurred(
        ID2D1DeviceContext deviceContext,
        ID2D1Bitmap1 source,
        float standardDeviation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(standardDeviation);

        using var blur = (ID2D1Effect)deviceContext.CreateEffect(EffectGuids.GaussianBlur);
        blur.SetValue((int)GaussianBlurProperties.StandardDeviation, standardDeviation);
        blur.SetValue((int)GaussianBlurProperties.Optimization, GaussianBlurOptimization.Balanced);
        blur.SetValue((int)GaussianBlurProperties.BorderMode, BorderMode.Soft);
        blur.SetInput(0, source, true);
        using ID2D1Image output = blur.Output;

        var pixelSize = new SizeI(source.PixelSize.Width, source.PixelSize.Height);
        return CreateTargetBitmap(deviceContext, pixelSize, DefaultDpi, () =>
        {
            deviceContext.DrawImage(
                output,
                Vector2.Zero,
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
