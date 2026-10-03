using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Dameview.App;
using Dameview.Commands;
using Dameview.Diagnostics;
using Dameview.Imaging.Decoding;
using Dameview.Imaging.Loading;
using Dameview.Installation;
using Dameview.Navigation;
using Dameview.Notifications;
using Dameview.Rendering;
using Dameview.Settings;
using Dameview.UI;
using Dameview.UI.Presentation;
using Dameview.Updates;
using Dameview.Viewing;
using Dameview.Win32;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace Dameview;

internal sealed class DameviewApp : IAppActions, ICommandHost, IDisposable
{
    private const long RenderBitmapCacheCapacityBytes = 256L * 1024L * 1024L;
    private const long ThumbnailBitmapCacheCapacityBytes = 64L * 1024L * 1024L;

    private readonly AppWindow _window;
    private readonly SynchronizationContext _uiContext;
    private readonly D2DRenderer _renderer;
    private readonly PerformanceMonitor _performanceMonitor;
    private readonly ViewerUi _ui;
    private readonly WindowsImageLoadingBackend _imageBackend;
    private readonly ThumbnailCoordinator _thumbnailCoordinator;
    private readonly ImageLoadService _imageLoadService;
    private readonly RenderBitmapCache _renderBitmapCache;
    private readonly RenderBitmapCache _thumbnailBitmapCache;
    private readonly ThumbnailImageLoader _thumbnailImageLoader;
    private readonly FolderSources _folderSources;
    private readonly HashSet<string> _decodableExtensions;
    private readonly ViewerWorkspace _workspace;
    private readonly SettingsService _settings;
    private readonly UpdateService _updates;
    // Asks often whether an automatic update check is due; the update service decides that it
    // rarely is. The first ask waits until startup is long done.
    private readonly Timer _updateCheckTimer;
    private readonly ToastService _toasts = new();
    private readonly FileActions _files;
    private Task<ID3D11Device>? _pendingHardwareDevice;
    private long _memorySampled;
    private int _pointerX;
    private int _pointerY;

    public DameviewApp(AppSettings startupSettings)
    {
        // Doesn't need the window, so it's created in parallel.
        Task<ID3D11Device> softwareDevice = Task.Run(
            static () => D2DRenderer.CreateDevice(DriverType.Warp));
        _window = new AppWindow("Dameview", 1100, 720, startupSettings.Window);
        _uiContext = new WindowSynchronizationContext(_window.Post);
        SynchronizationContext.SetSynchronizationContext(_uiContext);
        _window.SetTitleBarTheme(dark: true, Themes.Dark.Palette.WindowCaptionColor, Themes.Dark.Palette.WindowTextColor);
        _pointerX = _window.ClientWidth / 2;
        _pointerY = _window.ClientHeight / 2;
        StartupTrace.Mark("window");
        try
        {
            ID3D11Device device = softwareDevice.GetAwaiter().GetResult();
            StartupTrace.Mark("warp-device");
            _renderer = new D2DRenderer(
                _window.Handle,
                _window.ClientWidth,
                _window.ClientHeight,
                _window.Dpi,
                device);
        }
        catch (Exception exception)
        {
            Log.Error("Native", "Renderer initialization failed.", exception);
            throw;
        }

        // Loading the display driver dominates startup, so the window comes up on WARP and
        // switches once this finishes. Started only now because both contend for the loader lock.
        _pendingHardwareDevice = Task.Run(static () =>
        {
            long started = Stopwatch.GetTimestamp();
            ID3D11Device device = D2DRenderer.CreateDevice(DriverType.Hardware);
            Log.Debug("Startup", string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"hardware device created in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms"));
            return device;
        });

        _performanceMonitor = new PerformanceMonitor();
        _imageBackend = new WindowsImageLoadingBackend();
        _thumbnailCoordinator = new ThumbnailCoordinator(
            _imageBackend.LoadThumbnail,
            _uiContext);
        _imageLoadService = new ImageLoadService(
            _uiContext,
            _imageBackend,
            new ImageRepresentationPolicy(checked((int)_renderer.DeviceContext.MaximumBitmapSize)));
        _files = new FileActions(_window.Handle, _imageLoadService, _toasts);
        _renderBitmapCache = new RenderBitmapCache(RenderBitmapCacheCapacityBytes);
        _thumbnailBitmapCache = new RenderBitmapCache(ThumbnailBitmapCacheCapacityBytes);
        _thumbnailImageLoader = new ThumbnailImageLoader(
            _thumbnailCoordinator,
            _thumbnailBitmapCache,
            () => _renderer.DeviceContext,
            _uiContext);
        using var imageDecoder = new ImageDecoder();
        _decodableExtensions = imageDecoder.GetProbablySupportedExtensions();
        StartupTrace.Mark("wic");
        HashSet<string>.AlternateLookup<ReadOnlySpan<char>> decodableExtensions =
            _decodableExtensions.GetAlternateLookup<ReadOnlySpan<char>>();
        var folderScanner = new FolderScanner(
            path => decodableExtensions.Contains(Path.GetExtension(path)));
        _folderSources = new FolderSources(
            scope =>
            {
                var source = new FolderSource(scope, folderScanner, new FileSystemFolderWatcher(), _uiContext);
                source.WatcherFailed += exception =>
                    Log.Error("Folder", "Folder watcher failed.", exception);
                return source;
            },
            _uiContext);
        _workspace = new ViewerWorkspace(CreateTab);
        _settings = new SettingsService(SettingsService.DefaultPath, _uiContext, loaded: startupSettings);
        _updates = new UpdateService(
            new GitHubUpdateClient(),
            _uiContext,
            AppInstallation.GetInstalledRunningVersion());
        StartupTrace.Mark("services");
        _ui = new ViewerUi(
            _renderer.DeviceContext,
            _renderer.DirectWriteFactory,
            _workspace,
            _window.Dpi,
            Themes.Dark.Palette,
            this,
            _thumbnailImageLoader,
            _performanceMonitor,
            _toasts);
        StartupTrace.Mark("ui");
        _ui.Invalidated += _window.RequestRepaint;
        _ui.CursorChanged += _window.ApplyCursor;
        _workspace.ActivePaneChanged += HandleActivePaneChanged;
        _workspace.LayoutChanged += HandleLayoutChanged;
        _workspace.PaneRatiosChanged += HandlePaneRatiosChanged;
        _workspace.PaneActiveTabChanged += HandleActiveTabChanged;
        _workspace.PaneSessionStateChanged += HandleSessionChanged;
        _workspace.PaneTabsChanged += HandleTabsChanged;
        HandleTabsChanged(_workspace.ActivePane);

        _window.Shown += HandleWindowShown;
        _window.RenderFrame += HandleRenderFrame;
        _window.Resized += HandleResize;
        _window.SizeMoveStarted += _ui.BeginResize;
        _window.SizeMoveEnded += _ui.EndResize;
        _window.DpiChanged += HandleDpiChanged;
        _window.FileDragInput += HandleFileDragInput;
        _window.CopyDataReceived += HandleExternalInstanceMessage;
        _window.KeyPressed += HandleKeyPress;
        _window.TextInput += HandleTextInput;
        _window.PointerInput += HandlePointerInput;

        _settings.Changed += ApplySettings;
        _settings.Failed += error =>
        {
            _toasts.Notify(error, ToastSeverity.Error);
            _window.RequestRepaint();
        };
        _settings.ValuesIgnored += ignored =>
        {
            _toasts.Notify(DescribeIgnored(ignored), ToastSeverity.Warning);
            _window.RequestRepaint();
        };
        _updates.Changed += HandleUpdateChanged;
        _updates.UpdateDownloaded += HandleUpdateDownloaded;
        _updates.FoundInBackground += HandleUpdateFoundInBackground;
        _ui.ApplyUpdateState(_updates.State);
        _updateCheckTimer = new Timer(
            _ => _uiContext.Post(_ => CheckForUpdatesIfDue(), null),
            null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromHours(1));

        // The window was created with its placement already.
        ApplyPreferences(new AppSettings(), startupSettings);
        StartupTrace.Mark("wiring");
    }

    public int Run(string[] args)
    {
        _settings.Start();

        if (args.FirstOrDefault() is string imagePath)
        {
            _workspace.OpenImage(imagePath);
        }

        StartupTrace.Mark("args");
        _window.Closed += NativeMethods.RequestMessageLoopExit;
        try
        {
            return _window.Run(() => _renderer.FrameLatencyWaitHandle);
        }
        finally
        {
            _window.Closed -= NativeMethods.RequestMessageLoopExit;
        }
    }

    private void HandleFileDragInput(WindowFileDragEvent input)
    {
        if (input.Kind == WindowFileDragKind.Dropped)
        {
            Log.Debug("Workspace", $"Opened {input.Paths.Count} dropped file(s).");
        }

        _ui.HandleFileDrag(input);
        _window.RequestRepaint();
    }

    public void Dispose()
    {
        // WINDOWPLACEMENT keeps rcNormalPosition up to date while maximized,
        // so this also remembers the size that will be restored after unmaximizing.
        _settings.Update(_settings.Current with { Window = _window.CapturePlacement() });
        _updateCheckTimer.Dispose();
        _updates.Changed -= HandleUpdateChanged;
        _updates.UpdateDownloaded -= HandleUpdateDownloaded;
        _updates.FoundInBackground -= HandleUpdateFoundInBackground;
        _settings.Dispose();
        _files.Dispose();
        // Closing before the hardware device arrives leaves nothing else to own it.
        _ = _pendingHardwareDevice?.ContinueWith(
            static completed => completed.Result.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
        _ui.Invalidated -= _window.RequestRepaint;
        _ui.Dispose();
        _workspace.Dispose();
        _renderBitmapCache.Dispose();
        _thumbnailBitmapCache.Dispose();
        _imageLoadService.Dispose();
        _thumbnailCoordinator.Dispose();
        _renderer.Dispose();
        _window.Dispose();
    }

    public void OpenImage(string path)
    {
        Log.Debug("Workspace", $"Opened image: '{path}'.");
        _workspace.OpenImage(path);
    }

    public void SelectImage(string path)
    {
        Log.Debug("Workspace", $"Selected image: '{path}'.");
        _workspace.SelectImage(path);
    }

    public void OpenImageInNewTab(string path)
    {
        Log.Debug("Workspace", $"Opened image in new tab: '{path}'.");
        _workspace.OpenImageInNewTab(path);
    }

    private void Activate()
    {
        if (!_window.Activate())
        {
            Log.Warning("Window", "Windows refused to bring Dameview to the foreground.");
        }
    }

    private void HandleExternalInstanceMessage(nuint command, string message)
    {
        if (command == (nuint)SingleInstanceCommand.Activate)
        {
            Activate();
            return;
        }

        if (command != (nuint)SingleInstanceCommand.Open)
        {
            return;
        }

        // New tabs are appended to the active pane, so this is the first one opened here.
        int firstOpenedIndex = _workspace.Count;
        bool opened = false;
        foreach (string path in message.Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                OpenImageInNewTab(path);
                opened = true;
            }
        }

        if (opened)
        {
            _workspace.SelectTab(firstOpenedIndex);
            Activate();
        }
    }

    public void OpenImageInNewTab(string path, WorkspaceDropTarget target)
    {
        _workspace.OpenImageInNewTab(path, target);
    }

    public bool MoveTab(ViewerPane sourcePane, ViewerTab tab, WorkspaceDropTarget target)
    {
        return _workspace.MoveTab(sourcePane, tab, target);
    }

    public void SelectPane(ViewerPane pane)
    {
        _workspace.SelectPane(pane);
    }

    public void SelectTab(ViewerPane pane, int index)
    {
        _workspace.SelectTab(pane, index);
    }

    private void HandleKeyPress(WindowKeyEvent input)
    {
        // An element holding the keyboard, such as a shortcut being recorded, beats even the window bindings.
        if (_ui.HandleCapturedKey(input))
        {
            _window.RequestRepaint();
            return;
        }

        ViewerKeyBindings keyBindings = _settings.Current.KeyBindings;
        if (keyBindings.TryGetCommand(CommandScope.Window, input, out Command? command))
        {
            if (!input.IsRepeat || command.RepeatsWhileHeld)
            {
                Execute(command, ActiveContext);
            }

            return;
        }

        if (_ui.HandleKey(input))
        {
            _window.RequestRepaint();
            return;
        }

        if (input.Key == WindowKey.Escape && _window.IsFullscreen)
        {
            ToggleFullscreen();
            return;
        }

        if (keyBindings.TryGetCommand(CommandScope.Viewer, input, out command)
            && (!input.IsRepeat || command.RepeatsWhileHeld))
        {
            PointF anchor = _ui.GetImageViewportPoint(new PointF(_pointerX, _pointerY));
            Execute(command, CommandContext.For(_workspace.ActiveTab, anchor));
        }
    }

    private void HandleTextInput(string text)
    {
        if (_ui.HandleTextInput(text))
        {
            _window.RequestRepaint();
        }
    }

    public CommandContext ActiveContext => CommandContext.For(_workspace.ActiveTab);

    public bool CanExecute(Command command, CommandContext context) => command.CanExecute(this, context);

    public void Execute(Command command, CommandContext context) => command.Execute(this, context);

    ViewerWorkspace ICommandHost.Workspace => _workspace;

    ViewerUi ICommandHost.Ui => _ui;

    FileActions ICommandHost.Files => _files;

    void ICommandHost.CloseTab(ViewerTab tab)
    {
        ViewerPane pane = _workspace.PaneOf(tab);
        CloseTabOrPane(pane, pane.IndexOf(tab));
    }

    void ICommandHost.TogglePerformanceOverlay() => _performanceMonitor.Enabled = _ui.TogglePerformanceOverlay();

    // One bad value is worth naming; a mangled file is not worth four toasts.
    private static string DescribeIgnored(IReadOnlyList<string> ignored) => ignored.Count == 1
        ? $"Ignored {ignored[0]} in the settings file."
        : $"Ignored {ignored.Count} unreadable values in the settings file.";

    public void OpenPickedFile()
    {
        try
        {
            if (FilePicker.PickFile(_window.Handle, "Images", _decodableExtensions) is string path)
            {
                OpenImage(path);
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            Log.Error("Window", "The file picker could not be opened.", exception);
            _toasts.Notify("Could not open the file picker.", ToastSeverity.Error);
        }
    }

    public void ActivateUpdate() => _updates.Activate();

    public void UpdateSettings(Func<AppSettings, AppSettings> change) =>
        _settings.Update(change(_settings.Current));

    public void ToggleFullscreen()
    {
        _window.ToggleFullscreen();
        _ui.SetFullscreen(_window.IsFullscreen);
    }

    // Only for changes while running; the window was created with its initial placement.
    private void ApplySettings(AppSettings previous, AppSettings current)
    {
        if (current.Window is { } windowPlacement && previous.Window != windowPlacement)
        {
            _window.RestorePlacement(windowPlacement);
        }

        ApplyPreferences(previous, current);
    }

    /// <summary>Everything the settings decide except the window's own geometry.</summary>
    private void ApplyPreferences(AppSettings previous, AppSettings current)
    {
        Log.SetMinimumLevel(current.Logging.Level);
        _ui.ApplySettings(current);
        _workspace.AutoBalancePanes = current.AutoBalancePanes;
        if (!previous.KeyBindings.Equals(current.KeyBindings))
        {
            _ui.ApplyKeyBindings(current.KeyBindings);
        }

        if (previous.Theme != current.Theme)
        {
            Theme theme = Themes.Get(current.Theme);
            _ui.Palette = theme.Palette;
            _window.SetTitleBarTheme(theme.IsDark, theme.Palette.WindowCaptionColor, theme.Palette.WindowTextColor);
        }

        if (previous.Sort != current.Sort)
        {
            _workspace.SetSort(current.Sort);
        }

        if (previous.WheelZoomPercent != current.WheelZoomPercent)
        {
            _workspace.SetZoomStep(1.0 + (current.WheelZoomPercent / 100.0));
        }

        _window.RequestRepaint();
    }

    private void HandleUpdateChanged(UpdateState state)
    {
        // The settings panel shows the whole story, but a failure has to reach someone
        // who asked for an update and then closed it.
        if (state.Status == UpdateStatus.Failed)
        {
            _toasts.Notify(state.Error ?? "The update failed.", ToastSeverity.Error);
        }

        // Only a check that got an answer counts, so a failed one is retried at the next chance.
        if (state.Status is UpdateStatus.Current or UpdateStatus.Available)
        {
            UpdateSettings(settings => settings with { LastUpdateCheck = DateTimeOffset.UtcNow });
        }

        _ui.ApplyUpdateState(state);
        _window.RequestRepaint();
    }

    private void CheckForUpdatesIfDue()
    {
        AppSettings settings = _settings.Current;
        if (settings.CheckForUpdatesAutomatically)
        {
            _updates.CheckInBackgroundIfDue(settings.LastUpdateCheck, DateTimeOffset.UtcNow);
        }
    }

    private void HandleUpdateFoundInBackground(AppRelease release)
    {
        _toasts.Notify($"Dameview {release.Tag} is available. Update it from Settings › Updates.", ToastSeverity.Success);
        _window.RequestRepaint();
    }

    private void HandleUpdateDownloaded(string path)
    {
        AppUpdateApplier.Launch(path);
        _window.Close();
    }

    private void HandleSessionChanged(ViewerPane pane)
    {
        ViewerSessionState state = pane.ActiveSession.State;
        _ui.ApplyState(pane, state);
        if (ReferenceEquals(pane, _workspace.ActivePane))
        {
            string fileName = state.RequestedPath is null
                ? "Dameview"
                : Path.GetFileName(state.RequestedPath);
            _window.SetTitle($"{fileName} — Dameview");
        }

        _window.RequestRepaint();
    }

    private void HandleActiveTabChanged(ViewerPane pane)
    {
        _ui.BindTab(pane, pane.ActiveTab);
        HandleSessionChanged(pane);
    }

    private void HandleActivePaneChanged(ViewerPane pane)
    {
        _ui.BindActivePane(pane);
        HandleSessionChanged(pane);
    }

    private void HandleLayoutChanged(WorkspaceSplit? openingSplit)
    {
        _ui.ApplyLayout(_workspace.Root, openingSplit);
        _window.RequestRepaint();
    }

    private void HandlePaneRatiosChanged()
    {
        _ui.ApplyPaneRatios(_workspace.Root);
        _window.RequestRepaint();
    }

    private void CloseTabOrPane(ViewerPane pane, int index)
    {
        if (pane.Count > 1)
        {
            _workspace.CloseTab(pane, index);
            return;
        }

        if (_ui.BeginClosePane(pane, () => _workspace.CloseTab(pane, index)))
        {
            _workspace.ActivatePaneAfterClosing(pane);
            _window.RequestRepaint();
            return;
        }

        _window.Close();
    }

    private void HandleTabsChanged(ViewerPane pane)
    {
        _ui.ApplyTabs(
            pane,
            pane.Tabs
                .Select(tab => new ViewerTabInfo(
                    Path.GetFileName(tab.Session.State.RequestedPath) ?? "New tab",
                    tab.Session.State.RequestedPath))
                .ToArray(),
            pane.ActiveIndex);
    }

    private ViewerTab CreateTab()
    {
        ImageLoadClient loadClient = _imageLoadService.CreateClient();
        var imageLoader = new PresentationImageLoader(
            loadClient,
            _renderBitmapCache,
            () => _renderer.DeviceContext,
            _thumbnailImageLoader);
        var session = new ViewerSession(
            new FolderNavigator(),
            new FolderMonitor(_folderSources),
            imageLoader);
        return new ViewerTab(session);
    }

    private void HandleDpiChanged(float dpi)
    {
        _renderer.SetDpi(dpi);
        _ui.SetDpi(dpi);
    }

    // Driver and runtime calls for figures that barely move, so a few times a second is plenty.
    private bool ShouldSampleMemory(long frameStarted)
    {
        if (!_performanceMonitor.Enabled
            || Stopwatch.GetElapsedTime(_memorySampled, frameStarted) < TimeSpan.FromSeconds(0.5))
        {
            return false;
        }

        _memorySampled = frameStarted;
        return true;
    }

    private void HandleResize(int width, int height)
    {
        _renderer.Resize(width, height);
        _window.RequestRepaint();
    }

    private void HandlePointerInput(WindowPointerEvent input)
    {
        if (input.Kind is not (WindowPointerEventKind.Cancelled or WindowPointerEventKind.Left))
        {
            _pointerX = (int)input.Position.X;
            _pointerY = (int)input.Position.Y;
        }

        _ui.HandlePointer(input);
    }

    private void HandleWindowShown()
    {
        StartupTrace.Mark("shown");
        StartupTrace.Report();
        if (_pendingHardwareDevice is not { } pending)
        {
            return;
        }

        _ = pending.ContinueWith(
            completed => _uiContext.Post(_ => AdoptHardwareDevice(completed), null),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }

    private void AdoptHardwareDevice(Task<ID3D11Device> completed)
    {
        _pendingHardwareDevice = null;
        if (!completed.IsCompletedSuccessfully)
        {
            Log.Warning(
                "Native",
                $"Keeping the software rasterizer: {completed.Exception?.GetBaseException().Message}");
            return;
        }

        long started = Stopwatch.GetTimestamp();
        bool migrated = TryMoveCachedBitmaps(cache => cache.ReadBack(_renderer.DeviceContext));
        bool adopted = _renderer.AdoptDevice(completed.Result);
        ID2D1DeviceContext deviceContext = _renderer.DeviceContext;
        if (!(migrated && TryMoveCachedBitmaps(cache => cache.Upload(deviceContext))))
        {
            _renderBitmapCache.Clear();
            _thumbnailBitmapCache.Clear();
            // Before the UI rebinds, so no pane rebinds an image whose bitmap was just released.
            foreach (ViewerSession session in _workspace.Sessions)
            {
                session.ReloadDisplayedImage();
            }
        }

        _ui.RecreateDeviceResources(deviceContext);
        _window.RequestRepaint();
        if (adopted)
        {
            Log.Debug("Startup", string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"switched to the hardware device in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms"));
        }
    }

    // Every image representation keeps what it was built from, except a cached bitmap, whose
    // pixels only exist on the GPU: moving those is what lets the switch keep what is on screen
    // instead of decoding it again. Any failure only costs a visible reload.
    private bool TryMoveCachedBitmaps(Action<RenderBitmapCache> step)
    {
        try
        {
            step(_renderBitmapCache);
            step(_thumbnailBitmapCache);
            return true;
        }
        catch (Exception exception)
        {
            Log.Warning(
                "Native",
                $"Could not move cached images to the new device: {exception.Message}");
            return false;
        }
    }

    private void HandleRenderFrame()
    {
        long frameStarted = Stopwatch.GetTimestamp();
        bool sampleMemory = ShouldSampleMemory(frameStarted);
        VideoMemoryUsage? videoMemory = sampleMemory ? _renderer.QueryVideoMemory() : null;
        ProcessMemoryUsage? memory = sampleMemory
            ? new ProcessMemoryUsage(
                Environment.WorkingSet,
                GC.GetTotalMemory(forceFullCollection: false))
            : null;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int layoutPassesBefore = _ui.LayoutPasses;
        bool animationContinues = _ui.Update();
        _workspace.SyncViewports();
        TimeSpan updateTime = Stopwatch.GetElapsedTime(frameStarted);
        RenderTiming timing = _renderer.Render(
            _ui.DrawFrame,
            _ui.Palette.Background,
            _performanceMonitor.Enabled);
        StartupTrace.Mark("frame");
        _performanceMonitor.Record(new PerformanceFrameTiming(
            frameStarted,
            Stopwatch.GetElapsedTime(frameStarted, timing.SubmissionCompleted),
            timing.GpuTime,
            updateTime,
            _ui.LastLayoutTime,
            _ui.LayoutPasses - layoutPassesBefore,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            _ui.LastDrawnElements,
            _ui.LastDrawOperations,
            _ui.LastDrawTime,
            timing.SubmitTime,
            videoMemory,
            memory));

        if (animationContinues)
        {
            _window.RequestRepaint();
        }
        else if (_ui.NextAnimationFrameDelay is { } delay)
        {
            _window.RequestRepaintAfter(delay);
        }
    }
}

