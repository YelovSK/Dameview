namespace Dameview.UI.Animation;

internal sealed class UiAnimationClock
{
    private const double MaximumElapsedSeconds = 0.05;

    private readonly TimeProvider _timeProvider;
    private bool _hasTimestamp;
    private long _timestamp;

    internal UiAnimationClock(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal UiUpdateContext GetNextFrame()
    {
        long timestamp = _timeProvider.GetTimestamp();
        double elapsedSeconds = _hasTimestamp
            ? Math.Min(
                _timeProvider.GetElapsedTime(_timestamp, timestamp).TotalSeconds,
                MaximumElapsedSeconds)
            : 0.0;

        _timestamp = timestamp;
        _hasTimestamp = true;
        return new UiUpdateContext(elapsedSeconds);
    }

    internal void Reset()
    {
        _hasTimestamp = false;
    }
}
