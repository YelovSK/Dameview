using System.Drawing;
using Dameview.Imaging;
using Dameview.UI.Panels;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Dameview.UI;

internal sealed class ViewerUi : IUiElement, IDisposable
{
    private readonly ImagePanel _imagePanel;
    private readonly EmptyStatePanel _emptyStatePanel;
    private readonly StatusPanel _statusPanel;
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
        UiTheme theme)
    {
        _dpi = dpi;
        _imagePanel = new ImagePanel(deviceContext, width, height);
        _emptyStatePanel = new EmptyStatePanel(deviceContext, directWriteFactory, theme);
        _statusPanel = new StatusPanel(deviceContext, directWriteFactory, theme);
    }

    internal void SetImage(DecodedImage image, string path)
    {
        _capturedElement = null;
        _imagePanel.SetImage(image);
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
    }

    internal bool Update()
    {
        return _imagePanel.Update();
    }

    internal bool FitImage()
    {
        return _imagePanel.Fit();
    }

    internal bool ShowImageAtActualSize(float x, float y)
    {
        return _imagePanel.ShowActualSizeAt(x, y);
    }

    public void Draw(in UiDrawContext context, SizeF size)
    {
        bool showStatus = HasStatus;
        ViewerLayout layout = ViewerLayout.Calculate(size, _dpi, showStatus);
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
    }

    public bool HandlePointer(in UiPointerEvent input, SizeF size)
    {
        ViewerLayout layout = ViewerLayout.Calculate(size, _dpi, HasStatus);

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

        if (HasStatus && TryHandlePointer(_statusPanel, layout.Status, input))
        {
            return true;
        }

        return TryHandlePointer(GetContentElement(), layout.Content, input);
    }

    public void Dispose()
    {
        _statusPanel.Dispose();
        _emptyStatePanel.Dispose();
        _imagePanel.Dispose();
    }

    private bool HasStatus => _imagePanel.HasImage || _errorMessage is not null;

    private bool TryHandlePointer(
        IUiElement element,
        RectangleF bounds,
        in UiPointerEvent input)
    {
        if (!bounds.Contains(input.Position))
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

        return layout.Content;
    }

    private IUiElement GetContentElement()
    {
        return _imagePanel.HasImage ? _imagePanel : _emptyStatePanel;
    }
}
