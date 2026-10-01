using Dameview.Imaging.Loading;
using SharpGen.Runtime;
using SharpGen.Runtime.Win32;
using Vortice.WIC;

namespace Dameview.Imaging.Decoding;

internal sealed class ImageDecoder : IImageDecoder
{
    private const int BytesPerPixel = 4;

    private readonly IWICImagingFactory2 _factory = new();
    private readonly NativePixelBufferPool? _uploadPool;

    internal ImageDecoder(NativePixelBufferPool? uploadPool = null)
    {
        _uploadPool = uploadPool;
    }

    internal DecodedImage Decode(string path)
    {
        using IWICBitmapDecoder decoder = CreateDecoder(path, DecodeOptions.CacheOnLoad);
        return Decode(decoder);
    }

    // Reads the stored pixel dimensions from the file header. EXIF orientation is unnecessary for
    // choosing a tiled representation because rotation does not affect its size limits.
    internal ImageInfo GetInfo(string path)
    {
        using IWICBitmapDecoder decoder = CreateDecoder(path, DecodeOptions.CacheOnDemand);
        using IWICBitmapFrameDecode frame = decoder.GetFrame(0);
        return new ImageInfo(frame.Size.Width, frame.Size.Height, checked((int)decoder.FrameCount));
    }

    internal DecodedImageUpload DecodeUpload(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using IWICBitmapDecoder decoder = CreateDecoder(path, DecodeOptions.CacheOnLoad);
        using IWICBitmapFrameDecode frame = decoder.GetFrame(0);
        ImageOrientation orientation = GetOrientation(frame);
        using IWICFormatConverter converter = _factory.CreateFormatConverter();
        converter.Initialize(frame, PixelFormat.Format32bppPBGRA).CheckError();

        int width = frame.Size.Width;
        int height = frame.Size.Height;
        int stride = checked(width * BytesPerPixel);
        DecodedImageUpload upload = AllocateUpload(width, height, stride, orientation);
        try
        {
            converter.CopyPixels((uint)stride, upload.Span);
            cancellationToken.ThrowIfCancellationRequested();
            return upload;
        }
        catch
        {
            upload.Dispose();
            throw;
        }
    }

    internal DecodedImage Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using IWICStream wicStream = _factory.CreateStream(stream);
        using IWICBitmapDecoder decoder = _factory.CreateDecoderFromStream(
            wicStream,
            DecodeOptions.CacheOnLoad);
        return Decode(decoder);
    }

    private DecodedImage Decode(IWICBitmapDecoder decoder)
    {
        using IWICBitmapFrameDecode frame = decoder.GetFrame(0);
        using IWICFormatConverter converter = _factory.CreateFormatConverter();

        converter.Initialize(frame, PixelFormat.Format32bppPBGRA).CheckError();

        int width = frame.Size.Width;
        int height = frame.Size.Height;
        int stride = checked(width * BytesPerPixel);
        byte[] pixels = GC.AllocateUninitializedArray<byte>(checked(stride * height));
        converter.CopyPixels((uint)stride, pixels);

        return new DecodedImage(width, height, stride, pixels);
    }

    private IWICBitmapDecoder CreateDecoder(string path, DecodeOptions options)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The image could not be found.", fullPath);
        }

        return _factory.CreateDecoderFromFileName(fullPath, FileAccess.Read, options);
    }

    internal static ImageOrientation GetOrientation(IWICBitmapFrameDecode frame)
    {
        try
        {
            using IWICMetadataQueryReader reader = frame.MetadataQueryReader;
            foreach (string query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
            {
                try
                {
                    Variant metadata = reader.GetMetadataByName(query);
                    if (metadata.Value is ushort exifOrientation)
                    {
                        return ImageOrientation.FromExif(exifOrientation);
                    }
                }
                catch (SharpGenException)
                {
                    // Try the other common container-specific EXIF path.
                }
            }
        }
        catch (SharpGenException)
        {
            // Most images have no EXIF orientation tag. WIC reports that as
            // a metadata lookup failure, which should not prevent decoding.
        }

        return default;
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    private DecodedImageUpload AllocateUpload(
        int width,
        int height,
        int stride,
        ImageOrientation orientation)
    {
        return _uploadPool is null
            ? DecodedImageUpload.Allocate(width, height, stride, orientation)
            : DecodedImageUpload.Rent(_uploadPool, width, height, stride, orientation);
    }

    internal unsafe HashSet<string> GetProbablySupportedExtensions()
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using IEnumUnknown components = _factory.CreateComponentEnumerator(ComponentType.Decoder);
        var componentBuffer = new IUnknown[1];

        while (components.Next(componentBuffer) == 1)
        {
            if (componentBuffer[0] is not ComObject componentObject)
            {
                break;
            }

            using (componentObject)
            using (IWICBitmapCodecInfo? codec = componentObject.QueryInterfaceOrNull<IWICBitmapCodecInfo>())
            {
                if (codec is null)
                {
                    continue;
                }

                uint length = codec.GetFileExtensions(0, 0);
                if (length == 0)
                {
                    continue;
                }

                char[] buffer = new char[length];
                fixed (char* bufferPointer = buffer)
                {
                    _ = codec.GetFileExtensions(length, (nint)bufferPointer);
                    extensions.UnionWith(ParseFileExtensions(new string(bufferPointer)));
                }
            }

            componentBuffer[0] = null!;
        }

        return extensions;
    }

    internal static HashSet<string> ParseFileExtensions(string extensionList)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string value in extensionList.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            string extension = value.Trim().TrimEnd('\0').TrimStart('*').ToLowerInvariant();
            if (extension.Length > 1 && extension[0] == '.')
            {
                extensions.Add(extension);
            }
        }

        return extensions;
    }

    DecodedImageUpload IImageDecoder.DecodeUpload(
        string path,
        CancellationToken cancellationToken)
    {
        return DecodeUpload(path, cancellationToken);
    }

    ImageInfo IImageDecoder.GetInfo(string path)
    {
        return GetInfo(path);
    }
}

