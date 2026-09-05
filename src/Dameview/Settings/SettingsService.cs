using System.Text.Json;

namespace Dameview.Settings;

// Start, Update, reload delivery, and Dispose belong to the UI thread.
// Watcher callbacks only schedule a reload; they never mutate application state.
internal sealed class SettingsService : IDisposable
{
    private readonly string _path;
    private readonly Action<Action> _postToUi;
    private readonly object _gate = new();
    private readonly Timer _reloadTimer;
    private FileSystemWatcher? _watcher;
    private AppSettings? _fileSettings;
    private bool _disposed;
    private int _readAttempts;

    internal SettingsService(string path, Action<Action> postToUi)
    {
        _path = Path.GetFullPath(path);
        _postToUi = postToUi;
        _reloadTimer = new Timer(_ => _postToUi(Reload), null, Timeout.Infinite, Timeout.Infinite);
    }

    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Dameview", "settings.json");

    internal AppSettings Current { get; private set; } = new();
    internal string? Error { get; private set; }
    internal event Action<AppSettings, AppSettings>? Changed;
    internal event Action? ErrorChanged;

    internal void Start()
    {
        try
        {
            string directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Changed += ScheduleReload;
            _watcher.Created += ScheduleReload;
            _watcher.Deleted += ScheduleReload;
            _watcher.Renamed += ScheduleReload;
            _watcher.Error += (_, _) => ScheduleReload();
            _watcher.EnableRaisingEvents = true;
            if (!File.Exists(_path))
            {
                Save(Current);
            }

            Reload();
        }
        catch (Exception exception) when (IsSettingsError(exception))
        {
            SetError(exception.Message);
        }
    }

    internal void Update(AppSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        settings.Validate();
        Apply(settings);
        try
        {
            Save(settings);
            SetError(null);
        }
        catch (Exception exception) when (IsSettingsError(exception))
        {
            SetError($"Could not save settings: {exception.Message}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _reloadTimer.Dispose();
        }

        _watcher?.Dispose();
    }

    private void Reload()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            AppSettings settings = JsonSerializer.Deserialize(
                File.ReadAllText(_path), SettingsJsonContext.Default.AppSettings)
                ?? throw new JsonException("Settings must be a JSON object.");
            settings.Validate();
            _readAttempts = 0;
            // A delayed notification for our last save must not roll back a
            // newer live change whose save failed.
            if (settings == _fileSettings && settings != Current)
            {
                return;
            }

            _fileSettings = settings;
            Apply(settings);
            SetError(null);
        }
        catch (Exception exception) when (IsSettingsError(exception))
        {
            // Editors may briefly truncate, lock, or replace the file while saving.
            if (++_readAttempts < 4)
            {
                ScheduleReload();
                return;
            }

            _readAttempts = 0;
            SetError($"Could not load settings: {exception.Message}");
        }
    }

    private void Apply(AppSettings settings)
    {
        if (settings == Current)
        {
            return;
        }

        AppSettings previous = Current;
        Current = settings;
        Changed?.Invoke(previous, settings);
    }

    private void Save(AppSettings settings)
    {
        string temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
            File.Move(temporaryPath, _path, overwrite: true);
            _fileSettings = settings;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private void SetError(string? message)
    {
        if (message != Error)
        {
            Error = message;
            ErrorChanged?.Invoke();
        }
    }

    private void ScheduleReload(object sender, FileSystemEventArgs args)
    {
        ScheduleReload();
    }

    private void ScheduleReload()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _reloadTimer.Change(150, Timeout.Infinite);
            }
        }
    }

    private static bool IsSettingsError(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or JsonException;
    }
}
