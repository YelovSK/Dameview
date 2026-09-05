using System.Drawing;
using Dameview.Commands;
using Dameview.Imaging;
using Dameview.Navigation;
using Dameview.Platform;
using Dameview.Settings;
using Dameview.UI.Animation;
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
    private readonly StatusPanel _statusPanel;
    private readonly ToolbarPanel _toolbarPanel;
    private readonly SettingsPanel _settingsPanel;
    private readonly ModalHost _modalHost;
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
        _emptyStatePanel = new EmptyStatePanel(directWriteFactory);
        _statusPanel = new StatusPanel(directWriteFactory);
        _toolbarPanel = new ToolbarPanel(directWriteFactory, commands, theme.Design, ShowSettings);
        _modalHost = new ModalHost(CloseSettings);
        _settingsPanel = new SettingsPanel(
            directWriteFactory,
            CloseSettings,
            setTheme,
            setSort,
            theme.Design);

        AddChild(_imagePanel);
        AddChild(_emptyStatePanel);
        AddChild(_statusPanel);
        AddChild(_toolbarPanel);
        AddChild(_modalHost);
        _root = new UiRoot(this, dpi);

        bool hasImage = _state.DisplayedImage is not null;
        _imagePanel.IsVisible = hasImage;
        _emptyStatePanel.IsVisible = !hasImage;
        _toolbarPanel.HasImage = hasImage;
        _statusPanel.IsVisible = HasStatus;
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

    internal void ApplyState(ViewerSessionState state)
    {
        bool displayedImageChanged = !ReferenceEquals(_state.DisplayedImage, state.DisplayedImage);
        _state = state;
        if (displayedImageChanged && state.DisplayedImage is { } displayed)
        {
            _root.ClearPointer();
            ApplyDisplayedImage(displayed);
            _toolbarPanel.Show();
        }

        bool hasImage = state.DisplayedImage is not null;
        _imagePanel.IsVisible = hasImage;
        _emptyStatePanel.IsVisible = !hasImage;
        _toolbarPanel.HasImage = hasImage;
        _statusPanel.IsVisible = HasStatus;
        _root.InvalidateVisual();
    }

    internal void ApplySettings(AppSettings settings) => _settingsPanel.ApplySettings(settings);

    internal bool HandleKey(UiKeyEvent input)
    {
        if (_modalHost.IsOpen)
        {
            if (input.Key == UiKey.Escape)
            {
                return _modalHost.HandleEscape();
            }

            _root.HandleKey(input, _settingsPanel, wrapFocus: true, directionalNavigation: true);
            return true;
        }

        return _root.HandleKey(input, _toolbarPanel, wrapFocus: false, directionalNavigation: false);
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
        _imagePanel.Measure(availableSize);
        _emptyStatePanel.Measure(availableSize);
        _statusPanel.Measure(availableSize);
        _toolbarPanel.Measure(availableSize);
        _modalHost.Measure(availableSize);
        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        ViewerLayout layout = ViewerLayout.Calculate(
            finalSize,
            Palette.Design,
            HasStatus,
            showToolbar: true,
            toolbarWidthDips: _toolbarPanel.WidthDips);
        _imagePanel.Arrange(layout.Content);
        _emptyStatePanel.Arrange(layout.Content);
        _statusPanel.Arrange(layout.Status);
        _toolbarPanel.Arrange(layout.Toolbar);
        _modalHost.Arrange(new RectangleF(PointF.Empty, finalSize));
    }

    public void Dispose()
    {
        _root.ClearPointer();
        _root.SetFocus(null);
        _modalHost.Close();
        _settingsPanel.Dispose();
        _toolbarPanel.Dispose();
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
        _modalHost.Close();
        _root.SetFocus(_toolbarPanel.SettingsButton);
        _toolbarPanel.Show();
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
}
