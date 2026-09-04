using System.Runtime.InteropServices;
using Vortice.WIC;

namespace Dameview.Imaging;

internal static unsafe partial class WindowsThumbnail
{
    internal static DecodedImage? Load(string path)
    {
        Guid iid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
        int result = SHCreateItemFromParsingName(path, 0, in iid, out nint factory);
        if (result < 0)
        {
            return null;
        }

        nint bitmap = 0;
        try
        {
            // IShellItemImageFactory::GetImage follows the three IUnknown slots.
            var getImage = (delegate* unmanaged[Stdcall]<nint, ThumbnailSize, uint, nint*, int>)(*(nint**)factory)[3];
            const uint biggerSizeOk = 0x1;
            const uint thumbnailOnly = 0x8;
            const uint inCacheOnly = 0x10;
            result = getImage(factory, new ThumbnailSize(512, 512),
                biggerSizeOk | thumbnailOnly | inCacheOnly, &bitmap);
            if (result < 0 || bitmap == 0)
            {
                return null;
            }

            using var wic = new IWICImagingFactory2();
            using IWICBitmap source = wic.CreateBitmapFromHBITMAP(bitmap, 0,
                BitmapAlphaChannelOption.UsePremultipliedAlpha);
            using IWICFormatConverter converter = wic.CreateFormatConverter();
            converter.Initialize(source, PixelFormat.Format32bppPBGRA).CheckError();
            int width = source.Size.Width;
            int height = source.Size.Height;
            int stride = checked(width * 4);
            byte[] pixels = GC.AllocateUninitializedArray<byte>(checked(stride * height));
            converter.CopyPixels((uint)stride, pixels);
            return new DecodedImage(width, height, stride, pixels);
        }
        finally
        {
            if (bitmap != 0)
            {
                _ = DeleteObject(bitmap);
            }

            Marshal.Release(factory);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ThumbnailSize(int width, int height)
    {
        public readonly int Width = width;
        public readonly int Height = height;
    }

    [LibraryImport("shell32", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid iid, out nint item);

    [LibraryImport("gdi32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint value);
}
