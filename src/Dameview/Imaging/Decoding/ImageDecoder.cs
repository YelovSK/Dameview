using Dameview.Imaging.Loading;
using Dameview.Win32;
using SharpGen.Runtime;
using SharpGen.Runtime.Win32;
using Vortice.Mathematics;
using Vortice.WIC;

namespace Dameview.Imaging.Decoding;

internal sealed class ImageDecoder : IImageDecoder
{

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

    internal OpenedImage Open(string path) =>
        new(this, CreateDecoder(path, DecodeOptions.CacheOnDemand));

    private DecodedImageUpload DecodeUpload(
        IWICBitmapFrameDecode frame,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ImageOrientation orientation = GetOrientation(frame);
        using IWICFormatConverter converter = _factory.CreateFormatConverter();
        converter.Initialize(frame, PixelFormat.Format32bppPBGRA).CheckError();

        int width = frame.Size.Width;
        int height = frame.Size.Height;
        int stride = DecodedImage.GetStride(width);
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

    internal ClipboardBitmap DecodeClipboardBitmap(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using IWICBitmapDecoder decoder = CreateDecoder(path, DecodeOptions.CacheOnLoad);
        using IWICBitmapFrameDecode frame = decoder.GetFrame(0);
        ImageOrientation orientation = GetOrientation(frame);
        using IWICFormatConverter converter = _factory.CreateFormatConverter();
        converter.Initialize(frame, PixelFormat.Format32bppBGRA).CheckError();

        // Decoded into memory once, because the pixels are read back in small pieces and some
        // codecs would decode the image again for each one.
        using IWICBitmap stored = _factory.CreateBitmapFromSource(converter, BitmapCreateCacheOption.CacheOnLoad);
        cancellationToken.ThrowIfCancellationRequested();
        using IWICBitmapFlipRotator rotator = _factory.CreateBitmapFlipRotator();
        rotator.Initialize(stored, GetTransformOptions(orientation));
        return CopyToClipboardBitmap(rotator, cancellationToken);
    }

    // WIC mirrors first and then turns, the same as an orientation.
    internal static BitmapTransformOptions GetTransformOptions(ImageOrientation orientation)
    {
        BitmapTransformOptions turn = orientation.QuarterTurns switch
        {
            1 => BitmapTransformOptions.Rotate90,
            2 => BitmapTransformOptions.Rotate180,
            3 => BitmapTransformOptions.Rotate270,
            _ => BitmapTransformOptions.Rotate0,
        };
        return orientation.Mirrored ? turn | BitmapTransformOptions.FlipHorizontal : turn;
    }

    private static ClipboardBitmap CopyToClipboardBitmap(
        IWICBitmapSource source,
        CancellationToken cancellationToken)
    {
        SizeI size = source.Size;
        var bitmap = ClipboardBitmap.Allocate(size.Width, size.Height);
        try
        {
            for (int y = 0; y < size.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<byte> row = bitmap.GetRow(y);
                source.CopyPixels(new RectI(0, y, size.Width, 1), (uint)row.Length, row);
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
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
        int stride = DecodedImage.GetStride(width);
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

    ClipboardBitmap IImageDecoder.DecodeClipboardBitmap(
        string path,
        CancellationToken cancellationToken)
    {
        return DecodeClipboardBitmap(path, cancellationToken);
    }

    IOpenedImage IImageDecoder.Open(string path) => Open(path);

    internal sealed class OpenedImage : IOpenedImage
    {
        private readonly ImageDecoder _owner;
        private readonly IWICBitmapDecoder _decoder;
        private readonly IWICBitmapFrameDecode _frame;

        internal OpenedImage(ImageDecoder owner, IWICBitmapDecoder decoder)
        {
            _owner = owner;
            _decoder = decoder;
            try
            {
                _frame = decoder.GetFrame(0);
                // EXIF orientation is left for decoding, since rotation doesn't change the
                // size limits the header is read for.
                Info = new ImageInfo(_frame.Size.Width, _frame.Size.Height, checked((int)decoder.FrameCount));
            }
            catch
            {
                _frame?.Dispose();
                decoder.Dispose();
                throw;
            }
        }

        public ImageInfo Info { get; }

        public DecodedImageUpload DecodeUpload(CancellationToken cancellationToken = default) =>
            _owner.DecodeUpload(_frame, cancellationToken);

        public void Dispose()
        {
            _frame.Dispose();
            _decoder.Dispose();
        }
    }
}

