namespace Dameview.Updates;

internal closed record UpdateState
{
    internal sealed record Unavailable : UpdateState;

    internal sealed record ManagedByPackage : UpdateState;

    internal sealed record Idle : UpdateState;

    internal sealed record Checking : UpdateState;

    internal sealed record Current(AppRelease Release) : UpdateState;

    internal sealed record Available(AppRelease Release) : UpdateState;

    internal sealed record Downloading(AppRelease Release) : UpdateState;

    internal sealed record Applying(AppRelease Release) : UpdateState;

    // A failed download keeps its release so that trying again downloads it again.
    internal sealed record Failed(string Error, AppRelease? Release = null) : UpdateState;
}
