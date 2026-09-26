using System.Buffers.Binary;
using Dameview.Imaging.Animation;
using Dameview.Imaging.Decoding;
using Dameview.Imaging.Loading;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class WicWebpAnimationDecoderTests
{
    // Lossless 4x1 canvas: red, transparent delta, replacement with disposal,
    // then a half-transparent blue pixel and yellow. Durations are 20/40/60/80 ms.
    private const string Animation =
        "UklGRuoAAABXRUJQVlA4WAoAAAASAAAAAwAAAAAAQU5JTQYAAAAAAAAAAgBBTk1GKAAAAAAAAAAAAAMAAAAAABQAAAJWUDhMDwAAAC8DAAAABxD9j/4HIqL/AQBBTk1GKgAAAAEAAAAAAAEAAAAAACgAAABWUDhMEQAAAC8BAAAQDxAx//MfjApE9D8AAEFOTUYoAAAAAAAAAAAAAQAAAAAAPAAAA1ZQOEwQAAAALwEAABAPMP8R8x+MjOh/AEFOTUYsAAAAAQAAAAAAAQAAAAAAUAAAAlZQOEwTAAAALwEAABAPsP/7P/8P/I8lZET/AwA=";
    private const string StaticImage = "UklGRhwAAABXRUJQVlA4TA8AAAAvAwAAAAcQ/Y/+ByKi/wEA";

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void ComposesFramesAndPlaysTheExactNumberOfLoops(int loops)
    {
        string path = WriteAnimation((ushort)loops);
        try
        {
            using var decoder = new ImageDecoder();
            Assert.AreEqual(4, decoder.GetInfo(path).FrameCount);
            var backend = new WindowsImageLoadingBackend();
            Assert.IsTrue(backend.SupportsAnimation(path));
            using IAnimationSession session = backend.OpenAnimation(path);
            Assert.IsTrue(session.IsAnimated);
            var frames = new List<AnimationFrame> { session.FirstFrame };
            Assert.IsTrue(SpinWait.SpinUntil(() =>
            {
                while (session.TryGetReadyFrame(out AnimationFrame frame))
                {
                    frames.Add(frame);
                }

                return session.IsComplete;
            }, TimeSpan.FromSeconds(5)));
            while (session.TryGetReadyFrame(out AnimationFrame frame))
            {
                frames.Add(frame);
            }

            Assert.IsNull(session.Error);
            Assert.HasCount(4 * loops, frames);
            byte[][] expected =
            [
                [0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255],
                [0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 255, 0, 0, 255],
                [0, 255, 0, 255, 0, 0, 0, 0, 0, 0, 255, 255, 255, 0, 0, 255],
                [0, 0, 0, 0, 0, 0, 0, 0, 128, 0, 0, 128, 0, 255, 255, 255],
            ];
            for (int index = 0; index < frames.Count; index++)
            {
                Assert.AreEqual(4, frames[index].Image.Width);
                Assert.AreEqual(1, frames[index].Image.Height);
                Assert.AreEqual(TimeSpan.FromMilliseconds((index % 4 + 1) * 20), frames[index].Duration);
                CollectionAssert.AreEqual(expected[index % 4], frames[index].Image.Pixels);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void InfiniteAnimationRepeatsAndCanBeCancelledWithoutDrainingTheQueue()
    {
        string path = WriteAnimation(0);
        try
        {
            using IAnimationSession session = new WicWebpAnimationDecoder().Open(path);
            int count = 1;
            Assert.IsTrue(SpinWait.SpinUntil(() =>
            {
                if (session.TryGetReadyFrame(out _))
                {
                    count++;
                }

                return count >= 12;
            }, TimeSpan.FromSeconds(5)));
            Assert.IsFalse(session.IsComplete);
            session.Dispose();
            Assert.IsTrue(SpinWait.SpinUntil(() => session.IsComplete, TimeSpan.FromSeconds(5)));
            Assert.IsNull(session.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void StaticWebpDoesNotRequireAnimationMetadata()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Convert.FromBase64String(StaticImage));
            using var decoder = new ImageDecoder();
            Assert.AreEqual(1, decoder.GetInfo(path).FrameCount);
            using IAnimationSession session = new WicWebpAnimationDecoder().Open(path);
            Assert.IsFalse(session.IsAnimated);
            Assert.IsTrue(session.IsComplete);
            Assert.IsFalse(session.TryGetReadyFrame(out _));
            Assert.IsNull(session.Error);
            Assert.AreEqual(4, session.FirstFrame.Image.Width);
            CollectionAssert.AreEqual(
                new byte[] { 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255 },
                session.FirstFrame.Image.Pixels);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteAnimation(ushort loops)
    {
        byte[] bytes = Convert.FromBase64String(Animation);
        // ANIM's loop count follows its four-byte background color.
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(42, 2), loops);
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.WEBP");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
