using System.Drawing;
using Dameview.Commands;
using Dameview.Imaging;
using Dameview.Navigation;
using Dameview.Platform;
using Dameview.Settings;
using Dameview.UI.Animation;
using Dameview.UI.Layout;
using Dameview.UI.Panels;
using Dameview.Viewing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI;

internal sealed class ViewerUi : UiElement, IDisposable
{
    private readonly ID2D1DeviceContext _deviceContext;
    private readonly ID2D1SolidColorBrush _brush;
    private readonly ImagePanel _imagePanel;
    private readonly EmptyStatePanel _emptyStatePanel;
    private readonly Overlay _contentOverlay;
    private readonly Overlay _mainOverlay;
    private readonly SplitView _splitView;
    private readonly StatusPanel _statusPanel;
    private readonly ToolbarPanel _toolbarPanel;
    private readonly GalleryPanel _galleryPanel;
    private readonly SettingsPanel _settingsPanel;
    private readonly ModalHost _modalHost;
    private readonly PopupHost _popupHost;
    private readonly UiAnimationClock _animationClock;
    private readonly UiRoot _root;
    private ViewerSessionState _state;

    internal ViewerUi(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        ViewerSession session,
        float dpi,
        UiTheme theme,
        IViewerCommands commands,
        IThumbnailLoader thumbnailLoader,
        Action<ThemeMode> setTheme,
        Action<FolderSort> setSort,
        TimeProvider? timeProvider = null)
    {
        _deviceContext = deviceContext;
        _brush = deviceContext.CreateSolidColorBrush(default(Color4));
        Palette = theme;
        _animationClock = new UiAnimationClock(timeProvider);
        _state = session.State;
        _imagePanel = new ImagePanel(deviceContext, session.Viewport, session.Animator, timeProvider);
        _emptyStatePanel = new EmptyStatePanel(
            directWriteFactory,
            LoadApplicationIcon(deviceContext),
            ShowSettings);
        _contentOverlay = new Overlay(_imagePanel, _emptyStatePanel);
        _statusPanel = new StatusPanel(directWriteFactory);
        _toolbarPanel = new ToolbarPanel(directWriteFactory, commands, ShowSettings);
        _galleryPanel = new GalleryPanel(
            deviceContext,
            directWriteFactory,
            thumbnailLoader,
            commands.OpenImage);
        _mainOverlay = new Overlay(_contentOverlay, _statusPanel, _toolbarPanel);
        _splitView = new SplitView(
            _mainOverlay,
            _galleryPanel,
            initialDividerOffsetDips: GalleryPanel.DefaultWidthDips);
        _modalHost = new ModalHost(CloseSettings);
        _popupHost = new PopupHost();
        _settingsPanel = new SettingsPanel(
            directWriteFactory,
            _popupHost,
            CloseSettings,
            setTheme,
            setSort);

        AddChild(_splitView);
        AddChild(_modalHost);
        AddChild(_popupHost);
        _root = new UiRoot(this, dpi);
        _root.CursorChanged += cursor => _cursorChanged?.Invoke(cursor);

        bool hasImage = _state.DisplayedImage is not null;
        _imagePanel.IsVisible = hasImage;
        _emptyStatePanel.IsVisible = !hasImage;
        _toolbarPanel.IsVisible = hasImage;
        _statusPanel.IsVisible = HasStatus;
        _galleryPanel.IsVisible = _state.FolderEntries.Length > 0;
        _splitView.SecondPaneVisible = _galleryPanel.IsVisible;
        _galleryPanel.ApplyState(_state.FolderEntries, _state.RequestedPath);
        if (_state.DisplayedImage is { } displayed)
        {
            ApplyDisplayedImage(displayed);
            _toolbarPanel.Show();
        }
    }

    internal event Action? Invalidated
    {
        add => _root.Invalidated += value;
        remove => _root.Invalidated -= value;
    }

    private Action<UiCursor>? _cursorChanged;

    internal event Action<UiCursor>? CursorChanged
    {
        add => _cursorChanged += value;
        remove => _cursorChanged -= value;
    }

    internal UiTheme Palette { get; set; }
    internal string? SettingsError
    {
        get => _settingsPanel.Error;
        set
        {
            if (_settingsPanel.Error == value)
            {
                return;
            }

            _settingsPanel.Error = value;
            _statusPanel.IsVisible = HasStatus;
            _root.InvalidateVisual();
        }
    }

    internal TimeSpan? NextAnimationFrameDelay => _imagePanel.NextAnimationFrameDelay;

    internal PointF GetImageViewportPoint(PointF nativePoint)
    {
        PointF point = new(
            UiDpi.PixelsToDips(nativePoint.X, _root.Dpi),
            UiDpi.PixelsToDips(nativePoint.Y, _root.Dpi));
        RectangleF imageBounds = _imagePanel.GetBoundsRelativeTo(this);
        return new PointF(
            UiDpi.DipsToPixels(point.X - imageBounds.X, _root.Dpi),
            UiDpi.DipsToPixels(point.Y - imageBounds.Y, _root.Dpi));
    }

    internal void ApplyState(ViewerSessionState state)
    {
        bool hadDisplayedImage = _state.DisplayedImage is not null;
        bool displayedImageChanged = !ReferenceEquals(_state.DisplayedImage, state.DisplayedImage);
        _state = state;
        if (displayedImageChanged && state.DisplayedImage is { } displayed)
        {
            _root.ClearPointer();
            ApplyDisplayedImage(displayed);
            if (!hadDisplayedImage)
            {
                _toolbarPanel.Show();
            }
        }

        bool hasImage = state.DisplayedImage is not null;
        _imagePanel.IsVisible = hasImage;
        _emptyStatePanel.IsVisible = !hasImage;
        _toolbarPanel.IsVisible = hasImage;
        _statusPanel.IsVisible = HasStatus;
        _galleryPanel.IsVisible = state.FolderEntries.Length > 0;
        _splitView.SecondPaneVisible = _galleryPanel.IsVisible;
        _galleryPanel.ApplyState(state.FolderEntries, state.RequestedPath);
        _root.InvalidateVisual();
    }

    internal void ApplySettings(AppSettings settings) => _settingsPanel.ApplySettings(settings);

    internal bool HandleKey(UiKeyEvent input)
    {
        if (_popupHost.IsOpen)
        {
            if (input.Key == UiKey.Escape)
            {
                return _popupHost.HandleEscape();
            }

            if (input.Key == UiKey.Tab)
            {
                _popupHost.Close();
            }
        }

        if (_modalHost.IsOpen)
        {
            if (input.Key == UiKey.Escape)
            {
                return _modalHost.HandleEscape();
            }

            _root.HandleKey(input, _settingsPanel, wrapFocus: true, directionalNavigation: true);
            return true;
        }

        UiElement focusScope = _toolbarPanel.IsVisible ? _toolbarPanel : _emptyStatePanel;
        return _root.HandleKey(input, focusScope, wrapFocus: false, directionalNavigation: false);
    }

    internal void SetDpi(float dpi) => _root.SetDpi(dpi);

    internal bool Update()
    {
        UiUpdateContext context = _animationClock.GetNextFrame();
        bool continues = _root.Update(context);
        if (!continues && NextAnimationFrameDelay is null)
        {
            _animationClock.Reset();
        }

        return continues;
    }

    internal void DrawFrame(SizeF pixelSize)
    {
        UpdateStatus();
        var context = new UiDrawContext(_deviceContext, _brush, Palette, _root.Dpi);
        _root.Draw(context, pixelSize);
    }

    internal bool HandlePointer(in UiPointerEvent input) => _root.HandlePointer(input);

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        _splitView.Measure(availableSize);
        _modalHost.Measure(availableSize);
        _popupHost.Measure(availableSize);
        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        _splitView.Arrange(new RectangleF(PointF.Empty, finalSize));
        ViewerLayout layout = ViewerLayout.Calculate(
            _splitView.FirstPaneBounds.Size,
            HasStatus,
            showToolbar: _toolbarPanel.IsVisible,
            toolbarWidthDips: ToolbarPanel.WidthDips);
        _statusPanel.Arrange(layout.Status);
        _toolbarPanel.Arrange(layout.Toolbar);
        _modalHost.Arrange(new RectangleF(PointF.Empty, finalSize));
        _popupHost.Arrange(new RectangleF(PointF.Empty, finalSize));
    }

    public void Dispose()
    {
        _root.ClearPointer();
        _root.SetFocus(null);
        _modalHost.Close();
        _popupHost.Close();
        _settingsPanel.Dispose();
        _toolbarPanel.Dispose();
        _galleryPanel.Dispose();
        _statusPanel.Dispose();
        _emptyStatePanel.Dispose();
        _imagePanel.Dispose();
        _brush.Dispose();
    }

    private bool HasStatus => SettingsError is not null
        || _state.DisplayedImage is not null
        || _state.Message is not null;

    private void ShowSettings()
    {
        _root.ClearPointer();
        _root.SetFocus(null);
        _popupHost.Close();
        _modalHost.Show(_settingsPanel);
        _root.SetFocus(_settingsPanel.InitialFocus);
    }

    private void CloseSettings()
    {
        if (!_modalHost.IsOpen)
        {
            return;
        }

        _root.ClearPointer();
        _root.SetFocus(null);
        _popupHost.Close();
        _modalHost.Close();
        if (_toolbarPanel.IsVisible)
        {
            _root.SetFocus(_toolbarPanel.SettingsButton);
            _toolbarPanel.Show();
        }
        else
        {
            _root.SetFocus(_emptyStatePanel.SettingsButton);
        }
    }

    private void UpdateStatus()
    {
        if (!HasStatus)
        {
            return;
        }

        string? animationError = _imagePanel.AnimationError is { } exception
            ? $"Animation stopped: {exception.Message}"
            : null;
        _statusPanel.Status = new ViewerStatus(
            Path.GetFileName(_state.RequestedPath) ?? string.Empty,
            _state.DisplayedImage?.Image.Width ?? 0,
            _state.DisplayedImage?.Image.Height ?? 0,
            _imagePanel.ZoomPercentage,
            SettingsError ?? animationError ?? _state.Message,
            SettingsError is not null || animationError is not null || _state.IsError);
    }

    private void ApplyDisplayedImage(ImageLoaded displayed)
    {
        if (displayed.Animation is { } animation)
        {
            _imagePanel.SetAnimation(animation);
        }
        else
        {
            _imagePanel.SetImage(displayed.Image, displayed.IsPreview);
        }
    }

    private static ID2D1Bitmap1 LoadApplicationIcon(ID2D1DeviceContext deviceContext)
    {
        using Stream stream = typeof(ViewerUi).Assembly.GetManifestResourceStream(
            "Dameview.Assets.dameview.ico")
            ?? throw new InvalidOperationException("The embedded application icon could not be found.");
        using var decoder = new ImageDecoder();
        return D2DBitmapFactory.Create(deviceContext, decoder.Decode(stream));
    }
}
