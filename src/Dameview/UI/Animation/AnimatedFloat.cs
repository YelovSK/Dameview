namespace Dameview.UI.Animation;

internal sealed class AnimatedFloat
{
    private readonly double _response;
    private readonly float _completionDistance;

    internal AnimatedFloat(
        float initialValue,
        double response,
        float completionDistance = 0.001f)
    {
        Current = initialValue;
        Target = initialValue;
        _response = response;
        _completionDistance = completionDistance;
    }

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

    internal bool Update(double elapsedSeconds)
    {
        if (Current == Target)
        {
            return false;
        }

        if (elapsedSeconds > 0.0)
        {
            double blend = 1.0 - Math.Exp(-_response * elapsedSeconds);
            Current += (Target - Current) * (float)blend;

            if (MathF.Abs(Target - Current) <= _completionDistance)
            {
                Current = Target;
            }
        }

        return Current != Target;
    }
}
