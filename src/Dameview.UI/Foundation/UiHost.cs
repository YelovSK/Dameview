using System.Drawing;
using Dameview.UI.Animation;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Foundation;

/// <summary>Connects a UI tree to the Direct2D device it draws on and the clock that animates it.</summary>
/// <remarks>
/// The root is created before the tree is built, so children added afterwards attach to it
/// and can already reach <see cref="DeviceContext"/> while they are being created.
/// </remarks>
internal sealed class UiHost : IDisposable
{
    private readonly UiTextLayoutCache _textLayouts;
    private readonly UiAnimationClock _animationClock;
    private readonly UiDrawTally _drawTally = new();
    private ID2D1SolidColorBrush _brush;

    internal UiHost(
        UiElement content,
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        float dpi,
        UiTheme palette,
        TimeProvider? timeProvider = null)
    {
        DeviceContext = deviceContext;
        _brush = deviceContext.CreateSolidColorBrush(default(Color4));
        _textLayouts = new UiTextLayoutCache(directWriteFactory);
        _animationClock = new UiAnimationClock(timeProvider);
        Palette = palette;
        Root = new UiRoot(content, dpi, _textLayouts);
    }

    internal UiRoot Root { get; }
    /// <summary>The context the tree draws with, which is replaced when the graphics device is.</summary>
    internal ID2D1DeviceContext DeviceContext { get; private set; }
    internal UiTheme Palette { get; set; }
    internal int LastDrawnElements => _drawTally.Elements;
    internal int LastDrawOperations => _drawTally.Operations;

    /// <summary>Advances the tree's animations by the time since the previous update.</summary>
    /// <returns><see langword="true"/> while another update is needed.</returns>
    internal bool Update(bool animationsEnabled = true) =>
        Root.Update(_animationClock.GetNextFrame(animationsEnabled));

    /// <summary>Forgets the previous update, so the next one does not count the idle time before it.</summary>
    internal void ResetClock() => _animationClock.Reset();

    internal void Draw(SizeF pixelSize)
    {
        _drawTally.Reset();
        var context = new UiDrawContext(DeviceContext, _brush, _textLayouts, _drawTally, Palette, Root.Dpi);
        Root.Draw(context, pixelSize);
    }

    internal void RecreateDeviceResources(ID2D1DeviceContext deviceContext)
    {
        DeviceContext = deviceContext;
        _brush.Dispose();
        _brush = deviceContext.CreateSolidColorBrush(default(Color4));
        Root.InvalidateVisual();
    }

    public void Dispose()
    {
        Root.ClearPointer();
        Root.SetFocus(null);
        _textLayouts.Dispose();
        _brush.Dispose();
    }
}
