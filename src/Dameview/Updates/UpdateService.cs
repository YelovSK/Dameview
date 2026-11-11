using Dameview.Diagnostics;
using Dameview.Installation;

namespace Dameview.Updates;

internal sealed class UpdateService
{
    /// <summary>How long after a successful check the next automatic one is due.</summary>
    internal static readonly TimeSpan BackgroundCheckInterval = TimeSpan.FromDays(1);

    private readonly IUpdateClient _client;
    private readonly SynchronizationContext _uiContext;
    private readonly Version? _currentVersion;

    internal UpdateService(
        IUpdateClient client,
        SynchronizationContext uiContext,
        AppInstallKind installKind,
        Version? currentVersion)
    {
        _client = client;
        _uiContext = uiContext;
        _currentVersion = currentVersion;
        State = installKind switch
        {
            AppInstallKind.Installed when currentVersion is not null => new UpdateState.Idle(),
            AppInstallKind.Package => new UpdateState.ManagedByPackage(),
            _ => new UpdateState.Unavailable(),
        };
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
        if (State is UpdateState.Idle or UpdateState.Current
            && now - lastCheck >= BackgroundCheckInterval)
        {
            Check(inBackground: true);
        }
    }

    internal void Activate()
    {
        AppRelease? release = State switch
        {
            UpdateState.Available available => available.Release,
            UpdateState.Failed failed => failed.Release,
            _ => null,
        };
        if (release is not null)
        {
            Download(release);
        }
        else if (State is UpdateState.Idle or UpdateState.Current or UpdateState.Failed)
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

        SetState(new UpdateState.Checking());
        Log.Debug("Updates", "Checking for updates.");
        _ = Task.Run(() =>
        {
            ReleaseResult result;
            try
            {
                result = _client.GetLatestRelease();
            }
            catch (Exception exception)
            {
                Log.Error("Updates", "Checking for updates threw.", exception);
                result = new UpdateFailure(exception.Message);
            }

            PostToUi(() => FinishCheck(result, inBackground));
        });
    }

    private void FinishCheck(ReleaseResult result, bool inBackground)
    {
        switch (result)
        {
            case AppRelease release when release.Version > _currentVersion:
                Log.Info("Updates", $"Update available: {release.Version}.");
                SetState(new UpdateState.Available(release));
                if (inBackground)
                {
                    FoundInBackground?.Invoke(release);
                }

                break;

            case AppRelease release:
                Log.Debug("Updates", "No update available.");
                SetState(new UpdateState.Current(release));
                break;

            case UpdateFailure failure when inBackground:
                Log.Warning("Updates", $"Could not check for updates in the background: {failure.Message}");
                SetState(new UpdateState.Idle());
                break;

            case UpdateFailure failure:
                Fail("Could not check for updates", failure);
                break;
        }
    }

    private void Download(AppRelease release)
    {
        SetState(new UpdateState.Downloading(release));
        Log.Info("Updates", $"Downloading update {release.Version}.");
        _ = Task.Run(() =>
        {
            DownloadResult result;
            try
            {
                result = _client.Download(release);
            }
            catch (Exception exception)
            {
                Log.Error("Updates", "Downloading the update threw.", exception);
                result = new UpdateFailure(exception.Message);
            }

            PostToUi(() => FinishDownload(result, release));
        });
    }

    private void FinishDownload(DownloadResult result, AppRelease release)
    {
        switch (result)
        {
            case string path:
                Log.Info("Updates", $"Update {release.Version} downloaded.");
                Apply(path, release);
                break;

            case UpdateFailure failure:
                Fail("Could not download the update", failure, release);
                break;
        }
    }

    private void Apply(string path, AppRelease release)
    {
        try
        {
            UpdateDownloaded?.Invoke(path);
            SetState(new UpdateState.Applying(release));
        }
        catch (Exception exception)
        {
            Log.Error("Updates", "Could not start the update.", exception);
            SetState(new UpdateState.Failed($"Could not start the update: {exception.Message}", release));
        }
    }

    private void Fail(string operation, UpdateFailure failure, AppRelease? release = null)
    {
        string message = $"{operation}: {failure.Message}";
        Log.Error("Updates", message);
        SetState(new UpdateState.Failed(message, release));
    }

    private void SetState(UpdateState state)
    {
        State = state;
        Changed?.Invoke(state);
    }

    private void PostToUi(Action action) => _uiContext.Post(_ => action(), null);
}
