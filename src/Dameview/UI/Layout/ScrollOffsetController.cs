namespace Dameview.UI.Layout;

internal sealed class ScrollOffsetController
{
    private const double Response = 18.0;
    private const float CompletionDistance = 0.1f;
    private float _maximumOffset;
    private float _offset;
    private float _targetOffset;

    internal float Offset => _offset;
    internal float TargetOffset => _targetOffset;

    internal void SetMaximum(float maximumOffset)
    {
        _maximumOffset = MathF.Max(0.0f, maximumOffset);
        _offset = Math.Clamp(_offset, 0.0f, _maximumOffset);
        _targetOffset = Math.Clamp(_targetOffset, 0.0f, _maximumOffset);
    }

    internal bool ScrollBy(float amount) => SetTarget(_targetOffset + amount);

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

    internal bool SetImmediate(float offset)
    {
        float clamped = Math.Clamp(offset, 0.0f, _maximumOffset);
        bool changed = _offset != clamped || _targetOffset != clamped;
        _offset = clamped;
        _targetOffset = clamped;
        return changed;
    }

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
