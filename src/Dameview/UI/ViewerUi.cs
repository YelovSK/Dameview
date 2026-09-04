using System.Drawing;
using Dameview.Commands;
using Dameview.Imaging;
using Dameview.UI.Animation;
using Dameview.UI.Panels;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Dameview.UI;

internal sealed class ViewerUi : IUiElement, IDisposable
{
    private readonly ImagePanel _imagePanel;
    private readonly EmptyStatePanel _emptyStatePanel;
    private readonly StatusPanel _statusPanel;
    private readonly ToolbarPanel _toolbarPanel;
    private readonly UiAnimationClock _animationClock;
    private IUiElement? _capturedElement;
    private string _fileName = string.Empty;
    private string? _errorMessage;
    private float _dpi;

    internal ViewerUi(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        int width,
        int height,
        float dpi,
        UiTheme theme,
        IViewerCommands commands,
        TimeProvider? timeProvider = null)
    {
        _dpi = dpi;
        _animationClock = new UiAnimationClock(timeProvider);
        _imagePanel = new ImagePanel(deviceContext, width, height);
        _emptyStatePanel = new EmptyStatePanel(deviceContext, directWriteFactory, theme);
        _statusPanel = new StatusPanel(deviceContext, directWriteFactory, theme);
        _toolbarPanel = new ToolbarPanel(
            deviceContext,
            directWriteFactory,
            theme,
            commands,
            dpi);
    }

    internal void SetImage(DecodedImage image, string path)
    {
        _capturedElement = null;
        _imagePanel.SetImage(image);
        _toolbarPanel.Show();
        _fileName = Path.GetFileName(path);
        _errorMessage = null;
    }

    internal void ShowError(string message)
    {
        _errorMessage = message;
    }

    internal void SetViewportSize(int width, int height)
    {
        _imagePanel.SetViewportSize(width, height);
    }

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

    internal bool FitImage()
    {
        return _imagePanel.Fit();
    }

    internal bool ShowImageAtActualSize(float x, float y)
    {
        return _imagePanel.ShowActualSizeAt(x, y);
    }

    internal bool ShowImageAtActualSize()
    {
        return _imagePanel.ShowActualSize();
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
                _fileName,
                _imagePanel.ImageWidth,
                _imagePanel.ImageHeight,
                _imagePanel.ZoomPercentage,
                _errorMessage);
            context.DrawElement(_statusPanel, layout.Status);
        }

        if (showToolbar)
        {
            context.DrawElement(_toolbarPanel, layout.Toolbar);
        }
    }

    public bool HandlePointer(in UiPointerEvent input, SizeF size)
    {
        ViewerLayout layout = ViewerLayout.Calculate(size, _dpi, HasStatus, HasToolbar);

        if (_capturedElement is IUiElement capturedElement)
        {
            RectangleF capturedBounds = GetBounds(capturedElement, layout);
            bool handled = capturedElement.HandlePointer(
                input.ToLocal(capturedBounds),
                capturedBounds.Size);

            if (input.Kind == UiPointerEventKind.Released)
            {
                _capturedElement = null;
            }

            return handled;
        }

        if (HasToolbar
            && TryHandlePointer(
                _toolbarPanel,
                layout.Toolbar,
                input,
                allowOutside: input.Kind == UiPointerEventKind.Moved))
        {
            return true;
        }

        if (HasStatus && TryHandlePointer(_statusPanel, layout.Status, input))
        {
            return true;
        }

        return TryHandlePointer(GetContentElement(), layout.Content, input);
    }

    public void Dispose()
    {
        _toolbarPanel.Dispose();
        _statusPanel.Dispose();
        _emptyStatePanel.Dispose();
        _imagePanel.Dispose();
    }

    private bool HasStatus => _imagePanel.HasImage || _errorMessage is not null;
    private bool HasToolbar => _imagePanel.HasImage;

    private bool TryHandlePointer(
        IUiElement element,
        RectangleF bounds,
        in UiPointerEvent input,
        bool allowOutside = false)
    {
        if (!allowOutside && !bounds.Contains(input.Position))
        {
            return false;
        }

        bool handled = element.HandlePointer(input.ToLocal(bounds), bounds.Size);
        if (handled
            && input.Kind == UiPointerEventKind.Pressed
            && input.Button != PointerButton.None)
        {
            _capturedElement = element;
        }

        return handled;
    }

    private RectangleF GetBounds(IUiElement element, ViewerLayout layout)
    {
        if (ReferenceEquals(element, _statusPanel))
        {
            return layout.Status;
        }

        if (ReferenceEquals(element, _toolbarPanel))
        {
            return layout.Toolbar;
        }

        return layout.Content;
    }

    private IUiElement GetContentElement()
    {
        return _imagePanel.HasImage ? _imagePanel : _emptyStatePanel;
    }
}
