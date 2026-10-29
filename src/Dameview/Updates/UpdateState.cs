namespace Dameview.Updates;

internal enum UpdateStatus
{
    Unavailable,
    ManagedByPackage,
    Idle,
    Checking,
    Current,
    Available,
    Downloading,
    Applying,
    Failed,
}

internal sealed record UpdateState(
    UpdateStatus Status,
    AppRelease? Release = null,
    string? Error = null);
