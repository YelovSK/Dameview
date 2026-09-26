using Dameview.Imaging;
using Dameview.Imaging.Animation;

namespace Dameview.Tests.Imaging;

[TestClass]
public sealed class AnimatedImagePlayerTests
{
    [TestMethod]
    public void AdvancesOnlyAfterTheCurrentFrameDuration()
    {
        var session = new FakeSession(
            new AnimationFrame(Image(1), TimeSpan.FromMilliseconds(100)),
            new AnimationFrame(Image(2), TimeSpan.FromMilliseconds(100)),
            new AnimationFrame(Image(3), TimeSpan.FromMilliseconds(100)));
        var time = new ManualTimeProvider();
        var player = new AnimatedImagePlayer(session, time);
        player.Update();

        time.Advance(TimeSpan.FromMilliseconds(99));
        Assert.IsFalse(player.Update());
        Assert.AreEqual(1, player.CurrentImage.Pixels[0]);

        time.Advance(TimeSpan.FromMilliseconds(3));
        Assert.IsTrue(player.Update());
        Assert.AreEqual(2, player.CurrentImage.Pixels[0]);

        // Exactly one duration later, without the earlier overshoot carrying it further.
        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.IsTrue(player.Update());
        Assert.AreEqual(3, player.CurrentImage.Pixels[0]);
    }

    [TestMethod]
    public void StopsAfterAFiniteSequenceIsConsumed()
    {
        var session = new FakeSession(
            new AnimationFrame(Image(1), TimeSpan.FromMilliseconds(10)),
            new AnimationFrame(Image(2), TimeSpan.FromMilliseconds(10)));
        var time = new ManualTimeProvider();
        var player = new AnimatedImagePlayer(session, time);
        player.Update();

        time.Advance(TimeSpan.FromMilliseconds(10));
        Assert.IsTrue(player.Update());
        Assert.AreEqual(2, player.CurrentImage.Pixels[0]);
        time.Advance(TimeSpan.FromMilliseconds(10));
        Assert.IsFalse(player.Update());
        Assert.IsNull(player.NextFrameDelay);
    }

    [TestMethod]
    public void ExposesAWorkerFailureAfterReadyFramesAreConsumed()
    {
        var failure = new InvalidDataException("Broken frame");
        var session = new FakeSession(
            new AnimationFrame(Image(1), TimeSpan.FromMilliseconds(10)),
            error: failure);
        var time = new ManualTimeProvider();
        var player = new AnimatedImagePlayer(session, time);
        player.Update();

        time.Advance(TimeSpan.FromMilliseconds(10));
        Assert.IsFalse(player.Update());

        Assert.AreSame(failure, player.Error);
        Assert.IsNull(player.NextFrameDelay);
    }

    [TestMethod]
    public void DoesNotPlayTimeSpentPaused()
    {
        var session = new FakeSession(
            new AnimationFrame(Image(1), TimeSpan.FromMilliseconds(100)),
            new AnimationFrame(Image(2), TimeSpan.FromMilliseconds(100)));
        var time = new ManualTimeProvider();
        var player = new AnimatedImagePlayer(session, time);
        player.Update();

        time.Advance(TimeSpan.FromMilliseconds(60));
        player.Update();
        player.Pause();
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.IsFalse(player.Update());
        Assert.AreEqual(1, player.CurrentImage.Pixels[0]);

        time.Advance(TimeSpan.FromMilliseconds(41));
        Assert.IsTrue(player.Update());
        Assert.AreEqual(2, player.CurrentImage.Pixels[0]);
    }

    private static DecodedImage Image(byte value) => new(1, 1, 4, [value, value, value, value]);

    private sealed class FakeSession : IAnimationSession
    {
        private readonly Queue<AnimationFrame> _frames;

        internal FakeSession(AnimationFrame first, params AnimationFrame[] frames)
            : this(first, null, frames)
        {
        }

        internal FakeSession(AnimationFrame first, Exception? error, params AnimationFrame[] frames)
        {
            FirstFrame = first;
            Error = error;
            _frames = new Queue<AnimationFrame>(frames);
        }

        public AnimationFrame FirstFrame { get; }
        public bool IsAnimated => true;
        public bool IsComplete => _frames.Count == 0;
        public Exception? Error { get; }

        public bool TryGetReadyFrame(out AnimationFrame frame)
        {
            if (_frames.TryDequeue(out frame!))
            {
                return true;
            }

            frame = null!;
            return false;
        }

        public void Dispose()
        {
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        internal void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }
}
