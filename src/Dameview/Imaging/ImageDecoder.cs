using SharpGen.Runtime;
using SharpGen.Runtime.Win32;
using Vortice.WIC;

namespace Dameview.Imaging;

internal sealed class ImageDecoder : IImageDecoder
{
    private const int BytesPerPixel = 4;

    private readonly IWICImagingFactory2 _factory = new();
    private HashSet<string>? _probablySupportedExtensions;

    internal bool IsProbablySupported(string path)
    {
        _probablySupportedExtensions ??= GetProbablySupportedExtensions();
        return _probablySupportedExtensions.Contains(Path.GetExtension(path));
    }

    internal DecodedImage Decode(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The image could not be found.", fullPath);
        }

        using IWICBitmapDecoder decoder = _factory.CreateDecoderFromFileName(
            fullPath,
            FileAccess.Read,
            DecodeOptions.CacheOnLoad);
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

    public void Dispose()
    {
        _factory.Dispose();
    }

    private unsafe HashSet<string> GetProbablySupportedExtensions()
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

    DecodedImage IImageDecoder.Decode(string path)
    {
        return Decode(path);
    }
}

