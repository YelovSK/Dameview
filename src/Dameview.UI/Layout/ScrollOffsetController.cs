using Dameview.UI.Animation;
using Dameview.UI.Foundation;

namespace Dameview.UI.Layout;

/// <summary>Clamps and animates a scroll position toward a requested target.</summary>
internal sealed class ScrollOffsetController
{
    private readonly AnimatedFloat _offset = new(0.0f, Motion.Snappy, completionDistance: 0.1f);
    private float _maximumOffset;

    // Clamped because the spring can overshoot the ends slightly.
    internal float Offset => Math.Clamp(_offset.Current, 0.0f, _maximumOffset);
    internal float TargetOffset => _offset.Target;

    /// <summary>Sets the largest valid offset and clamps both current and target positions.</summary>
    internal void SetMaximum(float maximumOffset)
    {
        _maximumOffset = MathF.Max(0.0f, maximumOffset);
        if (_offset.Current > _maximumOffset)
        {
            _offset.SetValue(_maximumOffset);
        }

        _offset.SetTarget(Math.Clamp(TargetOffset, 0.0f, _maximumOffset));
    }

    /// <summary>Moves the target by a relative amount.</summary>
    /// <returns><see langword="true"/> when the target changed.</returns>
    internal bool ScrollBy(float amount) => SetTarget(TargetOffset + amount);

    /// <summary>Sets a clamped target that the current offset will approach smoothly.</summary>
    /// <returns><see langword="true"/> when the target changed.</returns>
    internal bool SetTarget(float offset) => _offset.SetTarget(Math.Clamp(offset, 0.0f, _maximumOffset));

    /// <summary>Sets both current and target offsets immediately, without animation.</summary>
    /// <returns><see langword="true"/> when either stored offset changed.</returns>
    internal bool SetImmediate(float offset) => _offset.SetValue(Math.Clamp(offset, 0.0f, _maximumOffset));

    /// <summary>Advances the current offset toward its target.</summary>
    /// <returns><see langword="true"/> while the offset is still moving, including the frame it lands.</returns>
    internal bool Update(in UiUpdateContext context)
    {
        float before = Offset;
        return _offset.Update(context) | (Offset != before);
    }
}
