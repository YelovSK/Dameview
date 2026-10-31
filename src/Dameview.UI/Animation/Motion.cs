namespace Dameview.UI.Animation;

/// <summary>How an <see cref="AnimatedFloat"/> springs toward its target.</summary>
/// <param name="Response">Roughly how long one swing of the spring takes, in seconds. Smaller is faster.</param>
/// <param name="Damping">
/// How quickly the swinging dies down. Below 1 the value overshoots its target and bounces back.
/// At 1 it settles without passing the target. Above 1 it starts out faster and eases into place
/// more gently, the more so the higher it is.
/// </param>
internal readonly record struct Motion(double Response, double Damping)
{
    internal static readonly Motion Snappy = new(0.13, 1.3);
    internal static readonly Motion Smooth = new(0.21, 1.3);
    internal static readonly Motion Springy = new(0.32, 0.7);
    internal static readonly Motion Bouncy = new(0.32, 0.6);

    internal Motion WithoutOvershoot => this with { Damping = Math.Max(Damping, 1.0) };
}
