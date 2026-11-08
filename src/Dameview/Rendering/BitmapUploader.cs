using System.Diagnostics.CodeAnalysis;
using Dameview.Imaging;
using Dameview.Imaging.Loading;
using Vortice.Direct2D1;
using Vortice.Direct3D11;

namespace Dameview.Rendering;

internal interface IBitmapUploader
{
    /// <summary>Any thread. Copies a static image's pixels to the GPU.</summary>
    public IUploadedBitmap Upload(ImageRepresentation image);

    /// <summary>Any thread.</summary>
    public IUploadedBitmap Upload(DecodedImageUpload image);
}

internal interface IUploadedBitmap : IDisposable
{
    /// <summary>
    /// Window thread. Fails when the device was switched since the upload, which makes the
    /// upload useless.
    /// </summary>
    public bool TryCreateBitmap([NotNullWhen(true)] out ID2D1Bitmap1? bitmap);
}

// Copying a large image to the GPU takes long enough to drop frames, so a worker does it.
// Direct3D allows that from any thread and Direct2D does not, so the worker builds a texture
// and the window thread only wraps it as a bitmap, which is cheap.
internal sealed class BitmapUploader(D2DRenderer renderer) : IBitmapUploader
{
    public IUploadedBitmap Upload(ImageRepresentation image) => image switch
    {
        UploadImageRepresentation upload => Upload(upload.Upload),
        DecodedImageRepresentation decoded => Upload(device => D2DBitmapFactory.CreateTexture(device, decoded.Image)),
        _ => throw new ArgumentException("Only static images can be uploaded.", nameof(image)),
    };

    public IUploadedBitmap Upload(DecodedImageUpload image) =>
        Upload(device => D2DBitmapFactory.CreateTexture(device, image));

    private UploadedBitmap Upload(Func<ID3D11Device, ID3D11Texture2D> createTexture)
    {
        ID3D11Device device = renderer.AcquireD3DDevice();
        try
        {
            return new UploadedBitmap(renderer, device, createTexture(device));
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    private sealed class UploadedBitmap(
        D2DRenderer renderer,
        ID3D11Device device,
        ID3D11Texture2D texture) : IUploadedBitmap
    {
        public bool TryCreateBitmap([NotNullWhen(true)] out ID2D1Bitmap1? bitmap)
        {
            bitmap = renderer.D3DDevice.NativePointer == device.NativePointer
                ? D2DBitmapFactory.Create(renderer.DeviceContext, texture)
                : null;
            return bitmap is not null;
        }

        public void Dispose()
        {
            texture.Dispose();
            device.Dispose();
        }
    }
}
