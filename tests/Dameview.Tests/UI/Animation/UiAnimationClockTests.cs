using Dameview.UI;
using Dameview.UI.Animation;

namespace Dameview.Tests.UI.Animation;

[TestClass]
public sealed class UiAnimationClockTests
{
    [TestMethod]
    public void ClockStartsAtZeroAndCapsLongFrameTimes()
    {
        var timeProvider = new ManualTimeProvider();
        var clock = new UiAnimationClock(timeProvider);

        Assert.AreEqual(0.0, clock.GetNextFrame().ElapsedSeconds);

        timeProvider.Advance(TimeSpan.FromMilliseconds(16));
        Assert.AreEqual(0.016, clock.GetNextFrame().ElapsedSeconds, 0.0001);

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(0.05, clock.GetNextFrame().ElapsedSeconds);

        clock.Reset();
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.AreEqual(0.0, clock.GetNextFrame().ElapsedSeconds);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            return _timestamp;
        }

        internal void Advance(TimeSpan elapsed)
        {
            _timestamp += elapsed.Ticks;
        }
    }
}
