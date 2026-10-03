using Dameview.Diagnostics;

namespace Dameview.Updates;

internal sealed class UpdateService
{
    /// <summary>How long after a successful check the next automatic one is due.</summary>
    internal static readonly TimeSpan BackgroundCheckInterval = TimeSpan.FromDays(1);

    private readonly IUpdateClient _client;
    private readonly SynchronizationContext _uiContext;
    private readonly Version? _currentVersion;

    internal UpdateService(IUpdateClient client, SynchronizationContext uiContext, Version? currentVersion)
    {
        _client = client;
        _uiContext = uiContext;
        _currentVersion = currentVersion;
        State = new UpdateState(currentVersion is null ? UpdateStatus.Unavailable : UpdateStatus.Idle);
    }

    internal event Action<UpdateState>? Changed;
    internal event Action<string>? UpdateDownloaded;
    /// <summary>A background check found a newer release, which nobody asked to hear about yet.</summary>
    internal event Action<AppRelease>? FoundInBackground;

    internal UpdateState State { get; private set; }

    /// <summary>
    /// Checks for a newer release if the last successful check is old enough. Unlike a check
    /// someone asked for, a failure here only goes to the log and leaves the state idle.
    /// </summary>
    internal void CheckInBackgroundIfDue(DateTimeOffset lastCheck, DateTimeOffset now)
    {
        if (State.Status is UpdateStatus.Idle or UpdateStatus.Current
            && now - lastCheck >= BackgroundCheckInterval)
        {
            Check(inBackground: true);
        }
    }

    internal void Activate()
    {
        if (State is { Status: UpdateStatus.Available or UpdateStatus.Failed, Release: { } release })
        {
            Download(release);
        }
        else if (State.Status is UpdateStatus.Idle or UpdateStatus.Current or UpdateStatus.Failed)
        {
            Check(inBackground: false);
        }
    }

    private void Check(bool inBackground)
    {
        if (_currentVersion is null)
        {
            return;
        }

        SetState(new UpdateState(UpdateStatus.Checking));
        Log.Debug("Updates", "Checking for updates.");
        _ = Task.Run(() =>
        {
            try
            {
                AppRelease release = _client.GetLatestRelease();
                UpdateStatus status = release.Version > _currentVersion
                    ? UpdateStatus.Available
                    : UpdateStatus.Current;
                if (status == UpdateStatus.Available)
                {
                    Log.Info("Updates", $"Update available: {release.Version}.");
                }
                else
                {
                    Log.Debug("Updates", "No update available.");
                }
                PostToUi(() =>
                {
                    SetState(new UpdateState(status, release));
                    if (inBackground && status == UpdateStatus.Available)
                    {
                        FoundInBackground?.Invoke(release);
                    }
                });
            }
            catch (Exception exception) when (inBackground)
            {
                Log.Warning("Updates", $"Could not check for updates in the background: {exception.Message}");
                PostToUi(() => SetState(new UpdateState(UpdateStatus.Idle)));
            }
            catch (Exception exception)
            {
                PostFailure("Could not check for updates", exception);
            }
        });
    }

    private void Download(AppRelease release)
    {
        SetState(new UpdateState(UpdateStatus.Downloading, release));
        Log.Info("Updates", $"Downloading update {release.Version}.");
        _ = Task.Run(() =>
        {
            try
            {
                string path = _client.Download(release);
                Log.Info("Updates", $"Update {release.Version} downloaded.");
                PostToUi(() => Apply(path, release));
            }
            catch (Exception exception)
            {
                PostFailure("Could not download the update", exception, release);
            }
        });
    }

    private void Apply(string path, AppRelease release)
    {
        try
        {
            UpdateDownloaded?.Invoke(path);
            SetState(new UpdateState(UpdateStatus.Applying, release));
        }
        catch (Exception exception)
        {
            Log.Error("Updates", "Could not start the update.", exception);
            SetState(new UpdateState(
                UpdateStatus.Failed,
                release,
                Error: $"Could not start the update: {exception.Message}"));
        }
    }

    private void PostFailure(string operation, Exception exception, AppRelease? release = null)
    {
        Log.Error("Updates", operation + ".", exception);
        PostToUi(() => SetState(new UpdateState(
            UpdateStatus.Failed,
            release,
            Error: $"{operation}: {exception.Message}")));
    }

    private void SetState(UpdateState state)
    {
        State = state;
        Changed?.Invoke(state);
    }

    private void PostToUi(Action action) => _uiContext.Post(_ => action(), null);
}
