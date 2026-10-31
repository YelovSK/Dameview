using Dameview.UI.Foundation;

namespace Dameview.UI.Animation;

/// <summary>A value that springs toward its target, keeping its speed when the target changes.</summary>
internal sealed class AnimatedFloat
{
    private readonly float _completionDistance;
    private float _velocity;

    internal AnimatedFloat(
        float initialValue,
        Motion motion,
        float completionDistance = 0.001f)
    {
        Current = initialValue;
        Target = initialValue;
        Motion = motion;
        _completionDistance = completionDistance;
    }

    /// <summary>How the value springs. Changing it mid-animation keeps the current speed.</summary>
    internal Motion Motion { get; set; }
    internal float Current { get; private set; }
    internal float Target { get; private set; }

    internal bool SetTarget(float target)
    {
        if (Target == target)
        {
            return false;
        }

        Target = target;
        return true;
    }

    internal bool SetValue(float value)
    {
        if (Current == value && Target == value && _velocity == 0.0f)
        {
            return false;
        }

        Current = value;
        Target = value;
        _velocity = 0.0f;
        return true;
    }

    internal bool Update(in UiUpdateContext context)
    {
        if (IsSettled)
        {
            return false;
        }

        if (!context.AnimationsEnabled)
        {
            SetValue(Target);
            return false;
        }

        if (context.ElapsedSeconds > 0.0)
        {
            Step(context.ElapsedSeconds);
        }

        return !IsSettled;
    }

    private bool IsSettled => Current == Target && _velocity == 0.0f;

    // Uses the exact solution of a damped spring, so long frames can't make it blow up.
    private void Step(double elapsed)
    {
        double frequency = 2.0 * Math.PI / Motion.Response;
        double damping = Motion.Damping;
        double offset = Current - Target;
        double velocity = _velocity;
        double decay = Math.Exp(-damping * frequency * elapsed);
        double nextOffset;
        double nextVelocity;
        if (damping > 1.0)
        {
            // The offset is the sum of a slowly and a quickly fading part.
            double spread = frequency * Math.Sqrt((damping * damping) - 1.0);
            double slowRate = (-damping * frequency) + spread;
            double fastRate = (-damping * frequency) - spread;
            double slowPart = (velocity - (fastRate * offset)) / (slowRate - fastRate);
            double fastPart = offset - slowPart;
            double slowDecay = Math.Exp(slowRate * elapsed);
            double fastDecay = Math.Exp(fastRate * elapsed);
            nextOffset = (slowPart * slowDecay) + (fastPart * fastDecay);
            nextVelocity = (slowRate * slowPart * slowDecay) + (fastRate * fastPart * fastDecay);
        }
        else if (damping == 1.0)
        {
            double slope = velocity + (frequency * offset);
            nextOffset = (offset + (slope * elapsed)) * decay;
            nextVelocity = (velocity - (frequency * slope * elapsed)) * decay;
        }
        else
        {
            double swing = frequency * Math.Sqrt(1.0 - (damping * damping));
            double cos = Math.Cos(swing * elapsed);
            double sin = Math.Sin(swing * elapsed);
            nextOffset = decay * ((offset * cos) + ((velocity + (damping * frequency * offset)) / swing * sin));
            nextVelocity = decay * ((velocity * cos)
                - (((frequency * frequency * offset) + (damping * frequency * velocity)) / swing * sin));
        }

        // Velocity over frequency is roughly how much further the value would still swing.
        if (Math.Abs(nextOffset) <= _completionDistance
            && Math.Abs(nextVelocity) <= _completionDistance * frequency)
        {
            Current = Target;
            _velocity = 0.0f;
            return;
        }

        Current = Target + (float)nextOffset;
        _velocity = (float)nextVelocity;
    }
}
