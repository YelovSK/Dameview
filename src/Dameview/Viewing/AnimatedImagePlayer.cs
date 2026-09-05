using Dameview.Imaging;

namespace Dameview.Viewing;

internal sealed class AnimatedImagePlayer : IDisposable
{
    private const int MaximumCatchUpFrames = 8;
    private readonly IAnimationSession _session;
    private readonly TimeProvider _timeProvider;
    private AnimationFrame _current;
    private double _remainingSeconds;
    private long _timestamp;
    private bool _finished;
    private bool _frameChanged;

    internal AnimatedImagePlayer(IAnimationSession session, TimeProvider? timeProvider = null)
    {
        _session = session;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _current = session.FirstFrame;
        _remainingSeconds = ToSeconds(_current.Duration);
        _timestamp = _timeProvider.GetTimestamp();
    }

    internal DecodedImage CurrentImage => _current.Image;
    internal Exception? Error { get; private set; }

    internal TimeSpan? NextFrameDelay => _finished
        ? null
        : TimeSpan.FromSeconds(Math.Max(0.01, _remainingSeconds));

    internal bool Update()
    {
        if (_finished)
        {
            return false;
        }

        long timestamp = _timeProvider.GetTimestamp();
        double elapsedSeconds = _timeProvider.GetElapsedTime(_timestamp, timestamp).TotalSeconds;
        _timestamp = timestamp;
        _remainingSeconds -= Math.Max(0.0, elapsedSeconds);
        bool changed = false;
        int transitions = 0;
        while (_remainingSeconds <= 0.0 && transitions++ < MaximumCatchUpFrames)
        {
            if (!_session.TryGetReadyFrame(out AnimationFrame next))
            {
                _finished = _session.IsComplete;
                if (_finished)
                {
                    Error = _session.Error;
                }

                return false;
            }

            _current = next;
            _remainingSeconds += ToSeconds(next.Duration);
            changed = true;
            _frameChanged = true;
        }

        return changed;
    }

    internal bool TryTakeUpdatedImage(out DecodedImage image)
    {
        image = _current.Image;
        bool changed = _frameChanged;
        _frameChanged = false;
        return changed;
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    private static double ToSeconds(TimeSpan duration)
    {
        return Math.Clamp(duration.TotalSeconds, 0.01, 60.0);
    }
}
