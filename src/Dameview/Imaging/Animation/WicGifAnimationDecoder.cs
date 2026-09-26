using SharpGen.Runtime;
using Vortice.WIC;

namespace Dameview.Imaging.Animation;

internal sealed class WicGifAnimationDecoder : WicAnimationDecoder
{
    public override bool CanDecode(string path) =>
        string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase);

    protected override IEnumerable<AnimationFrame> DecodeFrames(
        IWICImagingFactory2 factory,
        IWICBitmapDecoder decoder)
    {
        using IWICMetadataQueryReader metadata = decoder.MetadataQueryReader;
        int width = MetadataUInt(metadata, "/logscrdesc/Width");
        int height = MetadataUInt(metadata, "/logscrdesc/Height");
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("The GIF has an invalid canvas size.");
        }

        byte[] canvas = new byte[checked(width * height * 4)];
        for (int index = 0; index < decoder.FrameCount; index++)
        {
            yield return DecodeFrame(factory, decoder, index, canvas, width, height);
        }
    }

    private static AnimationFrame DecodeFrame(
        IWICImagingFactory2 factory,
        IWICBitmapDecoder decoder,
        int index,
        byte[] canvas,
        int width,
        int height)
    {
        using IWICBitmapFrameDecode frame = decoder.GetFrame((uint)index);
        using IWICFormatConverter converter = factory.CreateFormatConverter();
        converter.Initialize(frame, PixelFormat.Format32bppPBGRA).CheckError();

        int frameWidth = frame.Size.Width;
        int frameHeight = frame.Size.Height;
        int frameStride = checked(frameWidth * 4);
        byte[] pixels = GC.AllocateUninitializedArray<byte>(checked(frameStride * frameHeight));
        converter.CopyPixels((uint)frameStride, pixels);

        int left = MetadataUInt(frame, "/imgdesc/Left");
        int top = MetadataUInt(frame, "/imgdesc/Top");
        if (frameWidth > width || frameHeight > height ||
            left > width - frameWidth || top > height - frameHeight)
        {
            throw new InvalidDataException("A GIF frame lies outside the image canvas.");
        }

        int disposal = MetadataUInt(frame, "/grctlext/Disposal");
        bool hasTransparency = MetadataUInt(frame, "/grctlext/TransparencyFlag") != 0;
        byte[]? previousCanvas = disposal == 3 ? (byte[])canvas.Clone() : null;
        for (int y = 0; y < frameHeight; y++)
        {
            int sourceOffset = y * frameStride;
            int destinationOffset = ((top + y) * width + left) * 4;
            for (int x = 0; x < frameWidth; x++)
            {
                int sourcePixel = sourceOffset + x * 4;
                if (hasTransparency && pixels[sourcePixel + 3] == 0)
                {
                    continue;
                }

                Buffer.BlockCopy(pixels, sourcePixel, canvas, destinationOffset + x * 4, 4);
            }
        }

        byte[] snapshot = (byte[])canvas.Clone();
        int delay = MetadataUInt(frame, "/grctlext/Delay");
        if (disposal == 2)
        {
            for (int y = 0; y < frameHeight; y++)
            {
                Array.Clear(canvas, ((top + y) * width + left) * 4, frameStride);
            }
        }
        else if (previousCanvas is not null)
        {
            Buffer.BlockCopy(previousCanvas, 0, canvas, 0, canvas.Length);
        }

        return new AnimationFrame(
            new DecodedImage(width, height, width * 4, snapshot),
            TimeSpan.FromMilliseconds(Math.Clamp(delay * 10, 10, 60000)));
    }

    private static int MetadataUInt(IWICBitmapFrameDecode frame, string name)
    {
        try
        {
            using IWICMetadataQueryReader reader = frame.MetadataQueryReader;
            return MetadataUInt(reader, name);
        }
        catch (SharpGenException)
        {
            return 0;
        }
    }

    private static int MetadataUInt(IWICMetadataQueryReader reader, string name)
    {
        return Convert.ToInt32(
            reader.GetMetadataByName(name).Value,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    protected override int ReadLoopCount(IWICBitmapDecoder decoder)
    {
        try
        {
            using IWICMetadataQueryReader metadata = decoder.MetadataQueryReader;
            if (metadata.GetMetadataByName("/appext/application").Value is not byte[] application ||
                (!application.AsSpan().SequenceEqual("NETSCAPE2.0"u8) &&
                 !application.AsSpan().SequenceEqual("ANIMEXTS1.0"u8)) ||
                metadata.GetMetadataByName("/appext/data").Value is not byte[] data ||
                data.Length < 4 || data[1] != 1)
            {
                return 1;
            }

            int repetitions = data[2] | (data[3] << 8);
            return repetitions == 0 ? 0 : repetitions + 1;
        }
        catch (SharpGenException)
        {
            return 1;
        }
    }
}
