namespace Dameview.Updates;

internal sealed class UpdateService
{
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

    internal UpdateState State { get; private set; }

    internal void Activate()
    {
        if (State is { Status: UpdateStatus.Available or UpdateStatus.Failed, Release: { } release })
        {
            Download(release);
        }
        else if (State.Status is UpdateStatus.Idle or UpdateStatus.Current or UpdateStatus.Failed)
        {
            Check();
        }
    }

    private void Check()
    {
        if (_currentVersion is null)
        {
            return;
        }

        SetState(new UpdateState(UpdateStatus.Checking));
        _ = Task.Run(() =>
        {
            try
            {
                AppRelease release = _client.GetLatestRelease();
                UpdateStatus status = release.Version > _currentVersion
                    ? UpdateStatus.Available
                    : UpdateStatus.Current;
                PostToUi(() => SetState(new UpdateState(status, release)));
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
        _ = Task.Run(() =>
        {
            try
            {
                string path = _client.Download(release);
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
            SetState(new UpdateState(
                UpdateStatus.Failed,
                release,
                Error: $"Could not start the update: {exception.Message}"));
        }
    }

    private void PostFailure(string operation, Exception exception, AppRelease? release = null)
    {
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
