using Dameview.Imaging;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class WicGifAnimationDecoderTests
{
    [TestMethod]
    public void ComposesOffsetFramesAndPlaysTheConfiguredNumberOfLoops()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Convert.FromBase64String(
                "R0lGODlhBAABAIAAAAAAAP8AACH/C05FVFNDQVBFMi4wAwEBAAAh+QQFAQAAACwAAAAAAQABAAACAkwBACH5BAkBAAAALAEAAAABAAEAAAICTAEAIfkEDQEAAAAsAgAAAAEAAQAAAgJMAQAh+QQFAQAAACwDAAAAAQABAAACAkwBADs="));

            using IAnimationSession animation = new WicGifAnimationDecoder().Open(path);
            var frames = new List<AnimationFrame> { animation.FirstFrame };
            Assert.IsTrue(SpinWait.SpinUntil(() =>
            {
                while (animation.TryGetReadyFrame(out AnimationFrame frame))
                {
                    frames.Add(frame);
                }

                return animation.IsComplete;
            }, TimeSpan.FromSeconds(5)));
            while (animation.TryGetReadyFrame(out AnimationFrame frame))
            {
                frames.Add(frame);
            }

            Assert.IsNull(animation.Error);
            Assert.HasCount(8, frames);
            Assert.IsTrue(frames.All(frame => frame.Image.Width == 4 && frame.Image.Height == 1));
            CollectionAssert.AreEqual(new byte[] { 255, 255, 0, 0 }, Alpha(frames[1].Image));
            CollectionAssert.AreEqual(new byte[] { 255, 0, 255, 0 }, Alpha(frames[2].Image));
            CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255 }, Alpha(frames[3].Image));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] Alpha(DecodedImage image) =>
        [image.Pixels[3], image.Pixels[7], image.Pixels[11], image.Pixels[15]];
}
