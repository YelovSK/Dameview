using System.Drawing;

namespace Dameview.UI.Animation;

/// <summary>How an element animates in and out as its <c>IsPresent</c> changes.</summary>
/// <param name="Motion">How the transition springs between absent and present.</param>
/// <param name="Fade">Fades the element from transparent while it enters.</param>
/// <param name="HiddenOffset">Where the element is drawn from while absent, relative to its place.</param>
/// <param name="HiddenScale">The scale the element grows from while it enters.</param>
/// <param name="Collapse">
/// Shrinks the element's space in a container that supports it, so its neighbours move in to
/// fill it. The element is arranged into that shrinking space and clipped by it.
/// </param>
/// <param name="ExitMotion">
/// How the element springs away while leaving. Without one it leaves like it enters, minus any bounce.
/// </param>
internal readonly record struct UiTransition(
    Motion Motion,
    bool Fade = false,
    PointF HiddenOffset = default,
    float HiddenScale = 1.0f,
    bool Collapse = false,
    Motion? ExitMotion = null);
