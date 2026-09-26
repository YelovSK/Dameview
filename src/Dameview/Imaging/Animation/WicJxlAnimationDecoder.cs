using System.Globalization;
using Vortice.WIC;

namespace Dameview.Imaging.Animation;

internal sealed class WicJxlAnimationDecoder : WicAnimationDecoder
{
    private const string AnimationMetadata = "/{guid=501c2e24-7a7d-42b2-93c7-b4f45bcc92f7}";
    private const string FrameMetadata = "/{guid=958ecc2c-36cb-4af9-9ea8-0b74baccfd3e}";

    public override bool CanDecode(string path) =>
        string.Equals(Path.GetExtension(path), ".jxl", StringComparison.OrdinalIgnoreCase);

    protected override int ReadLoopCount(IWICBitmapDecoder decoder)
    {
        try
        {
            using IWICMetadataQueryReader metadata = decoder.MetadataQueryReader;
            return Convert.ToInt32(metadata.GetMetadataByName(AnimationMetadata + "/{ushort=1}").Value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (IsMetadataError(exception))
        {
            return 1;
        }
    }

    protected override IEnumerable<AnimationFrame> DecodeFrames(
        IWICImagingFactory2 factory,
        IWICBitmapDecoder decoder)
    {
        uint numerator = 0;
        uint denominator = 0;
        if (decoder.FrameCount > 1)
        {
            try
            {
                using IWICMetadataQueryReader metadata = decoder.MetadataQueryReader;
                numerator = ReadUInt(metadata, AnimationMetadata + "/{ushort=2}");
                denominator = ReadUInt(metadata, AnimationMetadata + "/{ushort=3}");
            }
            catch (Exception exception) when (IsMetadataError(exception))
            {
                // Invalid or unavailable timing falls back to 100 ms per frame.
            }
        }

        for (uint index = 0; index < decoder.FrameCount; index++)
        {
            using IWICBitmapFrameDecode frame = decoder.GetFrame(index);
            DecodedImage image = DecodePixels(factory, frame);

            var duration = TimeSpan.FromMilliseconds(100);
            if (numerator > 0 && denominator > 0)
            {
                try
                {
                    using IWICMetadataQueryReader metadata = frame.MetadataQueryReader;
                    duration = GetFrameDuration(ReadUInt(metadata, FrameMetadata + "/{ushort=1}"), numerator, denominator);
                }
                catch (Exception exception) when (IsMetadataError(exception))
                {
                    // Pixel decoding can still succeed without frame timing.
                }
            }

            yield return new AnimationFrame(image, duration);
        }
    }

    internal static TimeSpan GetFrameDuration(uint ticks, uint numerator, uint denominator) =>
        numerator == 0 || denominator == 0
            ? TimeSpan.FromMilliseconds(100)
            : TimeSpan.FromMilliseconds(Math.Clamp((double)ticks * denominator / numerator * 1000, 10, 60000));

    private static uint ReadUInt(IWICMetadataQueryReader metadata, string query) =>
        Convert.ToUInt32(metadata.GetMetadataByName(query).Value, CultureInfo.InvariantCulture);
}
