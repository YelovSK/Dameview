using Dameview.Imaging.Loading;
using SharpGen.Runtime;
using SharpGen.Runtime.Win32;
using Vortice.WIC;

namespace Dameview.Imaging.Decoding;

internal enum ExifOrientation : ushort
{
    Normal = 1,
    MirrorHorizontal = 2,
    Rotate180 = 3,
    MirrorVertical = 4,
    Transpose = 5,
    Rotate90Clockwise = 6,
    Transverse = 7,
    Rotate270Clockwise = 8,
}

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

    // Reads the stored pixel dimensions from the file header. EXIF orientation is deliberately not
    // applied: resolving it needs a metadata scan that is very slow for some formats (notably large
    // PNGs), and the preview infers a 90/270 swap from the thumbnail aspect instead.
    internal ImageInfo GetInfo(string path)
    {
        using IWICBitmapDecoder decoder = CreateDecoder(path, DecodeOptions.CacheOnDemand);
        using IWICBitmapFrameDecode frame = decoder.GetFrame(0);
        return new ImageInfo(frame.Size.Width, frame.Size.Height);
    }

    internal DecodedImageUpload DecodeUpload(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using IWICBitmapDecoder decoder = CreateDecoder(path, DecodeOptions.CacheOnLoad);
        using IWICBitmapFrameDecode frame = decoder.GetFrame(0);
        ExifOrientation orientation = GetExifOrientation(frame);
        using IWICFormatConverter converter = _factory.CreateFormatConverter();
        converter.Initialize(frame, PixelFormat.Format32bppPBGRA).CheckError();

        int width = frame.Size.Width;
        int height = frame.Size.Height;
        int stride = checked(width * BytesPerPixel);
        DecodedImageUpload source = AllocateUpload(width, height, stride);
        try
        {
            converter.CopyPixels((uint)stride, source.Span);
            cancellationToken.ThrowIfCancellationRequested();
            return ApplyExifOrientation(orientation, source, cancellationToken);
        }
        catch
        {
            source.Dispose();
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
        ExifOrientation orientation = GetExifOrientation(frame);
        using IWICFormatConverter converter = _factory.CreateFormatConverter();

        converter.Initialize(frame, PixelFormat.Format32bppPBGRA).CheckError();

        int width = frame.Size.Width;
        int height = frame.Size.Height;
        int stride = checked(width * BytesPerPixel);
        byte[] pixels = GC.AllocateUninitializedArray<byte>(checked(stride * height));
        converter.CopyPixels((uint)stride, pixels);

        return ApplyExifOrientation(orientation, width, height, stride, pixels);
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

    internal static ExifOrientation GetExifOrientation(IWICBitmapFrameDecode frame)
    {
        try
        {
            using IWICMetadataQueryReader reader = frame.MetadataQueryReader;
            foreach (string query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
            {
                try
                {
                    Variant metadata = reader.GetMetadataByName(query);
                    if (metadata.Value is ushort rawOrientation)
                    {
                        return Enum.IsDefined((ExifOrientation)rawOrientation)
                            ? (ExifOrientation)rawOrientation
                            : ExifOrientation.Normal;
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

        return ExifOrientation.Normal;
    }

    internal static BitmapTransformOptions MapExifOrientation(ExifOrientation orientation)
    {
        return orientation switch
        {
            ExifOrientation.MirrorHorizontal => BitmapTransformOptions.FlipHorizontal,
            ExifOrientation.Rotate180 => BitmapTransformOptions.Rotate180,
            ExifOrientation.MirrorVertical => BitmapTransformOptions.FlipVertical,
            ExifOrientation.Transpose => BitmapTransformOptions.FlipHorizontal | BitmapTransformOptions.Rotate270,
            ExifOrientation.Rotate90Clockwise => BitmapTransformOptions.Rotate90,
            ExifOrientation.Transverse => BitmapTransformOptions.FlipHorizontal | BitmapTransformOptions.Rotate90,
            ExifOrientation.Rotate270Clockwise => BitmapTransformOptions.Rotate270,
            _ => BitmapTransformOptions.Rotate0,
        };
    }

    internal static DecodedImage ApplyExifOrientation(
        ExifOrientation orientation,
        int width,
        int height,
        int sourceStride,
        byte[] source)
    {
        if (orientation == ExifOrientation.Normal)
        {
            return new DecodedImage(width, height, sourceStride, source);
        }

        bool swapsDimensions = orientation is ExifOrientation.Transpose
            or ExifOrientation.Rotate90Clockwise
            or ExifOrientation.Transverse
            or ExifOrientation.Rotate270Clockwise;
        int outputWidth = swapsDimensions ? height : width;
        int outputHeight = swapsDimensions ? width : height;
        int outputStride = checked(outputWidth * BytesPerPixel);
        byte[] output = GC.AllocateUninitializedArray<byte>(checked(outputStride * outputHeight));

        for (int y = 0; y < outputHeight; y++)
        {
            for (int x = 0; x < outputWidth; x++)
            {
                (int sourceX, int sourceY) = orientation switch
                {
                    ExifOrientation.MirrorHorizontal => (width - 1 - x, y),
                    ExifOrientation.Rotate180 => (width - 1 - x, height - 1 - y),
                    ExifOrientation.MirrorVertical => (x, height - 1 - y),
                    ExifOrientation.Transpose => (y, x),
                    ExifOrientation.Rotate90Clockwise => (y, height - 1 - x),
                    ExifOrientation.Transverse => (width - 1 - y, height - 1 - x),
                    ExifOrientation.Rotate270Clockwise => (width - 1 - y, x),
                    _ => (x, y),
                };

                Buffer.BlockCopy(
                    source,
                    sourceY * sourceStride + sourceX * BytesPerPixel,
                    output,
                    y * outputStride + x * BytesPerPixel,
                    BytesPerPixel);
            }
        }

        return new DecodedImage(outputWidth, outputHeight, outputStride, output);
    }

    private DecodedImageUpload ApplyExifOrientation(
        ExifOrientation orientation,
        DecodedImageUpload source,
        CancellationToken cancellationToken)
    {
        if (orientation == ExifOrientation.Normal)
        {
            return source;
        }

        bool swapsDimensions = orientation is ExifOrientation.Transpose
            or ExifOrientation.Rotate90Clockwise
            or ExifOrientation.Transverse
            or ExifOrientation.Rotate270Clockwise;
        int outputWidth = swapsDimensions ? source.Height : source.Width;
        int outputHeight = swapsDimensions ? source.Width : source.Height;
        int outputStride = checked(outputWidth * BytesPerPixel);
        DecodedImageUpload output = AllocateUpload(
            outputWidth,
            outputHeight,
            outputStride);
        try
        {
            Span<byte> sourcePixels = source.Span;
            Span<byte> outputPixels = output.Span;
            for (int y = 0; y < outputHeight; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int x = 0; x < outputWidth; x++)
                {
                    (int sourceX, int sourceY) = orientation switch
                    {
                        ExifOrientation.MirrorHorizontal => (source.Width - 1 - x, y),
                        ExifOrientation.Rotate180 => (source.Width - 1 - x, source.Height - 1 - y),
                        ExifOrientation.MirrorVertical => (x, source.Height - 1 - y),
                        ExifOrientation.Transpose => (y, x),
                        ExifOrientation.Rotate90Clockwise => (y, source.Height - 1 - x),
                        ExifOrientation.Transverse => (source.Width - 1 - y, source.Height - 1 - x),
                        ExifOrientation.Rotate270Clockwise => (source.Width - 1 - y, x),
                        _ => (x, y),
                    };
                    int sourceOffset = sourceY * source.Stride + sourceX * BytesPerPixel;
                    int outputOffset = y * outputStride + x * BytesPerPixel;
                    sourcePixels.Slice(sourceOffset, BytesPerPixel)
                        .CopyTo(outputPixels.Slice(outputOffset, BytesPerPixel));
                }
            }

            source.Dispose();
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    private DecodedImageUpload AllocateUpload(int width, int height, int stride)
    {
        return _uploadPool is null
            ? DecodedImageUpload.Allocate(width, height, stride)
            : DecodedImageUpload.Rent(_uploadPool, width, height, stride);
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

