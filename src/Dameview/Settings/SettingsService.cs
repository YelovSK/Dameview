using Dameview.Diagnostics;
using Dameview.Serialization;

namespace Dameview.Settings;

// Start, Update, reload delivery, and Dispose belong to the owning thread.
// Watcher callbacks only schedule a reload onto that thread; they never mutate application state.
internal sealed class SettingsService : IDisposable
{
    private readonly string _path;
    private readonly SynchronizationContext _ownerContext;
    private readonly Lock _gate = new();
    private readonly Timer _reloadTimer;
    private readonly TimeSpan _reloadDelay;
    private readonly TimeSpan _retryDelay;
    private FileSystemWatcher? _watcher;
    private AppSettings? _fileSettings;
    private bool _disposed;
    private int _readAttempts;
    private string[] _reportedIgnored = [];

    internal SettingsService(
        string path,
        SynchronizationContext ownerContext,
        SettingsServiceOptions? options = null)
    {
        _path = Path.GetFullPath(path);
        _ownerContext = ownerContext;
        options ??= new SettingsServiceOptions();
        _reloadDelay = options.ReloadDelay;
        _retryDelay = options.RetryDelay;
        _reloadTimer = new Timer(
            _ => _ownerContext.Post(_ => Reload(), null),
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Dameview", "settings.ini");

    internal AppSettings Current { get; private set; } = new();
    internal string? Error { get; private set; }
    internal event Action<AppSettings, AppSettings>? Changed;

    /// <summary>Raised for every failed operation, including one that fails the same way twice.</summary>
    internal event Action<string>? Failed;

    /// <summary>Raised when a file loaded, but parts of it were unreadable and used defaults.</summary>
    internal event Action<IReadOnlyList<string>>? ValuesIgnored;

    internal static AppSettings LoadForStartup()
    {
        try
        {
            if (!File.Exists(DefaultPath))
            {
                return new AppSettings();
            }

            // Nothing is listening this early, so a fallback here is only worth logging.
            (AppSettings settings, _) = SettingsIniSerializer.Read(File.ReadAllText(DefaultPath));
            settings.Validate();
            return settings;
        }
        catch (Exception exception) when (IsSettingsError(exception))
        {
            Log.Error("Settings", "Could not load startup settings.", exception);
            return new AppSettings();
        }
    }

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
            if (Error is null)
            {
                Log.Info("Settings", "Settings loaded.");
            }
        }
        catch (Exception exception) when (IsSettingsError(exception))
        {
            Log.Error("Settings", "Could not initialize settings.", exception);
            Fail(exception.Message);
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
            Error = null;
        }
        catch (Exception exception) when (IsSettingsError(exception))
        {
            Log.Error("Settings", "Could not save settings.", exception);
            Fail($"Could not save settings: {exception.Message}");
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
            (AppSettings settings, IReadOnlyList<string> ignored) =
                SettingsIniSerializer.Read(File.ReadAllText(_path));
            settings.Validate();
            _readAttempts = 0;

            // A delayed notification for our last save must not roll back a
            // newer live change whose save failed.
            if (settings == _fileSettings && settings != Current)
            {
                return;
            }

            _fileSettings = settings;

            // The watcher reports one edit more than once, and a file of nothing but bad
            // values still parses to the defaults, so the complaints are what must differ.
            if (ignored.Count > 0 && !ignored.SequenceEqual(_reportedIgnored))
            {
                ValuesIgnored?.Invoke(ignored);
            }

            _reportedIgnored = [.. ignored];
            Apply(settings);
            Log.Debug("Settings", "Settings reloaded.");
            Error = null;
        }
        catch (Exception exception) when (IsSettingsError(exception))
        {
            // Editors may briefly truncate, lock, or replace the file while saving.
            if (++_readAttempts < 4)
            {
                ScheduleRetry();
                return;
            }

            _readAttempts = 0;
            Log.Error("Settings", "Could not load settings.", exception);
            Fail($"Could not load settings: {exception.Message}");
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
            File.WriteAllText(temporaryPath, SettingsIniSerializer.Write(settings));
            File.Move(temporaryPath, _path, overwrite: true);
            _fileSettings = settings;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private void Fail(string message)
    {
        Error = message;
        Failed?.Invoke(message);
    }

    private void ScheduleReload(object sender, FileSystemEventArgs args)
    {
        ScheduleReload();
    }

    private void ScheduleReload()
    {
        ScheduleReload(_reloadDelay);
    }

    private void ScheduleRetry()
    {
        ScheduleReload(_retryDelay);
    }

    private void ScheduleReload(TimeSpan delay)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _reloadTimer.Change(delay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private static bool IsSettingsError(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or IniFormatException;
    }
}
