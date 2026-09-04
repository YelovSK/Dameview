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
    private IUiElement? _capturedElement;

    internal ViewerUi(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        int width,
        int height,
        UiTheme theme)
    {
        _imagePanel = new ImagePanel(deviceContext, width, height);
        _emptyStatePanel = new EmptyStatePanel(deviceContext, directWriteFactory, theme);
    }

    internal void SetImage(DecodedImage image)
    {
        _capturedElement = null;
        _imagePanel.SetImage(image);
    }

    internal void SetViewportSize(int width, int height)
    {
        _imagePanel.SetViewportSize(width, height);
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
        ViewerLayout layout = ViewerLayout.Calculate(size);
        context.DrawElement(GetContentElement(), layout.Content);
    }

    public bool HandlePointer(in UiPointerEvent input, SizeF size)
    {
        ViewerLayout layout = ViewerLayout.Calculate(size);
        IUiElement element = _capturedElement ?? GetContentElement();
        RectangleF bounds = layout.Content;

        if (_capturedElement is null && !bounds.Contains(input.Position))
        {
            return false;
        }

        UiPointerEvent localInput = input.ToLocal(bounds);
        bool handled = element.HandlePointer(localInput, bounds.Size);

        if (handled
            && input.Kind == UiPointerEventKind.Pressed
            && input.Button != PointerButton.None)
        {
            _capturedElement = element;
        }

        if (input.Kind == UiPointerEventKind.Released)
        {
            _capturedElement = null;
        }

        return handled;
    }

    public void Dispose()
    {
        _emptyStatePanel.Dispose();
        _imagePanel.Dispose();
    }

    private IUiElement GetContentElement()
    {
        return _imagePanel.HasImage ? _imagePanel : _emptyStatePanel;
    }
}
