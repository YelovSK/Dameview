using System.Globalization;
using Vortice.WIC;

namespace Dameview.Imaging.Animation;

internal sealed class WicWebpAnimationDecoder : WicAnimationDecoder
{
    public override bool CanDecode(string path) =>
        string.Equals(Path.GetExtension(path), ".webp", StringComparison.OrdinalIgnoreCase);

    protected override int ReadLoopCount(IWICBitmapDecoder decoder)
    {
        try
        {
            using IWICMetadataQueryReader metadata = decoder.MetadataQueryReader;
            return Convert.ToInt32(metadata.GetMetadataByName("/anim/{ushort=1}").Value, CultureInfo.InvariantCulture);
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
        for (uint index = 0; index < decoder.FrameCount; index++)
        {
            using IWICBitmapFrameDecode frame = decoder.GetFrame(index);
            DecodedImage image = DecodePixels(factory, frame);

            int duration = 0;
            if (decoder.FrameCount > 1)
            {
                try
                {
                    using IWICMetadataQueryReader metadata = frame.MetadataQueryReader;
                    duration = Convert.ToInt32(
                        metadata.GetMetadataByName("/anmf/{ushort=1}").Value,
                        CultureInfo.InvariantCulture);
                }
                catch (Exception exception) when (IsMetadataError(exception))
                {
                    duration = 100;
                }
            }

            // WIC returns the composed canvas, including blending and frame disposal.
            yield return new AnimationFrame(
                image,
                TimeSpan.FromMilliseconds(Math.Clamp(duration, 10, 60000)));
        }
    }
}
