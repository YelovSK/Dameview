namespace Dameview.UI.Layout;

/// <summary>Clamps and animates a scroll position toward a requested target.</summary>
internal sealed class ScrollOffsetController
{
    private const double Response = 18.0;
    private const float CompletionDistance = 0.1f;
    private float _maximumOffset;
    private float _offset;
    private float _targetOffset;

    internal float Offset => _offset;
    internal float TargetOffset => _targetOffset;

    /// <summary>Sets the largest valid offset and clamps both current and target positions.</summary>
    internal void SetMaximum(float maximumOffset)
    {
        _maximumOffset = MathF.Max(0.0f, maximumOffset);
        _offset = Math.Clamp(_offset, 0.0f, _maximumOffset);
        _targetOffset = Math.Clamp(_targetOffset, 0.0f, _maximumOffset);
    }

    /// <summary>Moves the target by a relative amount.</summary>
    /// <returns><see langword="true"/> when the target changed.</returns>
    internal bool ScrollBy(float amount) => SetTarget(_targetOffset + amount);

    /// <summary>Sets a clamped target that the current offset will approach smoothly.</summary>
    /// <returns><see langword="true"/> when the target changed.</returns>
    internal bool SetTarget(float offset)
    {
        float clamped = Math.Clamp(offset, 0.0f, _maximumOffset);
        if (_targetOffset == clamped)
        {
            return false;
        }

        _targetOffset = clamped;
        return true;
    }

    /// <summary>Sets both current and target offsets immediately, without animation.</summary>
    /// <returns><see langword="true"/> when either stored offset changed.</returns>
    internal bool SetImmediate(float offset)
    {
        float clamped = Math.Clamp(offset, 0.0f, _maximumOffset);
        bool changed = _offset != clamped || _targetOffset != clamped;
        _offset = clamped;
        _targetOffset = clamped;
        return changed;
    }

    /// <summary>Advances the current offset toward its target.</summary>
    /// <returns><see langword="true"/> while the current offset is not yet at its target.</returns>
    internal bool Update(double elapsedSeconds)
    {
        if (_offset == _targetOffset)
        {
            return false;
        }

        if (elapsedSeconds > 0.0)
        {
            double blend = 1.0 - Math.Exp(-Response * elapsedSeconds);
            _offset += (_targetOffset - _offset) * (float)blend;
            if (MathF.Abs(_targetOffset - _offset) <= CompletionDistance)
            {
                _offset = _targetOffset;
            }
        }

        return true;
    }
}
