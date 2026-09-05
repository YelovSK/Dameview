using System.Drawing;
using Dameview.Commands;
using Dameview.UI.Animation;
using Dameview.UI.Panels;
using Dameview.Viewing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI;

internal sealed class ViewerUi : IUiElement, IDisposable
{
    private readonly ID2D1DeviceContext _deviceContext;
    private readonly ID2D1SolidColorBrush _brush;
    private readonly ImagePanel _imagePanel;
    private readonly EmptyStatePanel _emptyStatePanel;
    private readonly StatusPanel _statusPanel;
    private readonly ToolbarPanel _toolbarPanel;
    private readonly UiAnimationClock _animationClock;
    private readonly UiPointerRouter _pointerRouter = new();
    private ViewerSessionState _state;
    private float _dpi;

    internal ViewerUi(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        ViewerSession session,
        float dpi,
        UiTheme theme,
        IViewerCommands commands,
        TimeProvider? timeProvider = null)
    {
        _deviceContext = deviceContext;
        _brush = deviceContext.CreateSolidColorBrush(default(Color4));
        _dpi = dpi;
        Palette = theme;
        _animationClock = new UiAnimationClock(timeProvider);
        _state = session.State;
        _imagePanel = new ImagePanel(deviceContext, session.Viewport, session.Animator);
        _emptyStatePanel = new EmptyStatePanel(directWriteFactory);
        _statusPanel = new StatusPanel(directWriteFactory);
        _toolbarPanel = new ToolbarPanel(
            directWriteFactory,
            commands,
            dpi);
        if (_state.DisplayedImage is { } displayed)
        {
            _imagePanel.SetImage(displayed.Image, displayed.IsPreview);
            _toolbarPanel.Show();
        }
    }

    internal void ApplyState(ViewerSessionState state)
    {
        if (!ReferenceEquals(_state.DisplayedImage, state.DisplayedImage)
            && state.DisplayedImage is { } displayed)
        {
            _pointerRouter.Cancel();
            _imagePanel.SetImage(displayed.Image, displayed.IsPreview);
            _toolbarPanel.Show();
        }

        _state = state;
    }

    internal UiTheme Palette { get; set; }
    internal string? SettingsError { get; set; }

    internal void SetDpi(float dpi)
    {
        _dpi = dpi;
        _toolbarPanel.SetDpi(dpi);
    }

    internal bool Update()
    {
        UiUpdateContext context = _animationClock.GetNextFrame();
        bool continues = Update(context);
        if (!continues)
        {
            _animationClock.Reset();
        }

        return continues;
    }

    public bool Update(in UiUpdateContext context)
    {
        bool continues = _imagePanel.Update(context);
        if (HasToolbar)
        {
            continues |= _toolbarPanel.Update(context);
        }

        return continues;
    }

    internal void DrawFrame(SizeF size)
    {
        var context = new UiDrawContext(_deviceContext, _brush, Palette, _dpi);
        Draw(context, size);
    }

    public void Draw(in UiDrawContext context, SizeF size)
    {
        bool showStatus = HasStatus;
        bool showToolbar = HasToolbar;
        ViewerLayout layout = ViewerLayout.Calculate(size, _dpi, showStatus, showToolbar);
        context.DrawElement(GetContentElement(), layout.Content);

        if (showStatus)
        {
            _statusPanel.Status = new ViewerStatus(
                Path.GetFileName(_state.RequestedPath) ?? string.Empty,
                _state.DisplayedImage?.Image.Width ?? 0,
                _state.DisplayedImage?.Image.Height ?? 0,
                _imagePanel.ZoomPercentage,
                SettingsError ?? _state.Message,
                SettingsError is not null || _state.IsError);
            context.DrawElement(_statusPanel, layout.Status);
        }

        if (showToolbar)
        {
            context.DrawElement(_toolbarPanel, layout.Toolbar);
        }
    }

    public UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size)
    {
        if (input.Kind == UiPointerEventKind.Cancelled)
        {
            return _pointerRouter.Cancel();
        }

        ViewerLayout layout = ViewerLayout.Calculate(size, _dpi, HasStatus, HasToolbar);
        if (_pointerRouter.Captured is IUiElement captured)
        {
            return _pointerRouter.DispatchCaptured(input, GetBounds(captured, layout));
        }

        UiPointerResult result = default;
        if (HasToolbar)
        {
            result = _pointerRouter.Route(_toolbarPanel, layout.Toolbar, input,
                observeOutside: input.Kind == UiPointerEventKind.Moved);
            if (result.Consumed)
            {
                return result;
            }
        }

        UiPointerResult content = _pointerRouter.Route(GetContentElement(), layout.Content, input);
        return content with { NeedsRepaint = result.NeedsRepaint || content.NeedsRepaint };
    }

    public void Dispose()
    {
        _pointerRouter.Cancel();
        _toolbarPanel.Dispose();
        _statusPanel.Dispose();
        _emptyStatePanel.Dispose();
        _imagePanel.Dispose();
        _brush.Dispose();
    }

    private bool HasStatus => SettingsError is not null || _state.DisplayedImage is not null || _state.Message is not null;
    private bool HasToolbar => _state.DisplayedImage is not null;

    private RectangleF GetBounds(IUiElement element, ViewerLayout layout)
    {
        if (ReferenceEquals(element, _toolbarPanel))
        {
            return layout.Toolbar;
        }

        return layout.Content;
    }

    private IUiElement GetContentElement()
    {
        return _state.DisplayedImage is not null ? _imagePanel : _emptyStatePanel;
    }
}
