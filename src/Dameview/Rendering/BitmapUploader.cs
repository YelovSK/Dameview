using Dameview.Imaging.Loading;
using Vortice.Direct2D1;
using Vortice.Direct3D11;

namespace Dameview.Rendering;

// Copying a large image to the GPU takes long enough to drop frames, so a worker does it.
// Direct3D allows that from any thread and Direct2D does not, so the worker builds a texture
// and the window thread only wraps it as a bitmap, which is cheap.
internal sealed class BitmapUploader(D2DRenderer renderer)
{
    /// <summary>
    /// Call on the window thread, which the task also completes on. The pixels must stay
    /// alive until it does.
    /// </summary>
    internal Task<ID2D1Bitmap1> CreateAsync(ImageRepresentation image) => image switch
    {
        UploadImageRepresentation upload =>
            CreateAsync(device => D2DBitmapFactory.CreateTexture(device, upload.Upload)),
        DecodedImageRepresentation decoded =>
            CreateAsync(device => D2DBitmapFactory.CreateTexture(device, decoded.Image)),
        _ => throw new ArgumentException("Only static images can be uploaded.", nameof(image)),
    };

    private async Task<ID2D1Bitmap1> CreateAsync(Func<ID3D11Device, ID3D11Texture2D> createTexture)
    {
        while (true)
        {
            // A reference of our own, because switching devices releases the renderer's while
            // the worker may still be using it.
            using ID3D11Device device = renderer.D3DDevice.QueryInterface<ID3D11Device>();
            using ID3D11Texture2D texture = await Task.Run(() => createTexture(device)).ConfigureAwait(true);

            // A texture from a device that was switched away from meanwhile can't be drawn.
            if (renderer.D3DDevice.NativePointer == device.NativePointer)
            {
                return D2DBitmapFactory.Create(renderer.DeviceContext, texture);
            }
        }
    }
}
