using System.Drawing;
using System.Numerics;
using Dameview.UI.Animation;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
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

    /// <summary>Whether animations play or jump straight to their end.</summary>
    internal bool AnimationsEnabled
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            ResetClock();
            Root.InvalidateVisual();
        }
    } = true;

    /// <summary>Advances the tree's animations by the time since the previous update.</summary>
    /// <returns><see langword="true"/> while another update is needed.</returns>
    internal bool Update() => Root.Update(_animationClock.GetNextFrame(AnimationsEnabled));

    /// <summary>Forgets the previous update, so the next one does not count the idle time before it.</summary>
    internal void ResetClock() => _animationClock.Reset();

    internal void Draw(SizeF pixelSize)
    {
        _drawTally.Reset();
        var context = new UiDrawContext(DeviceContext, _brush, _textLayouts, _drawTally, Palette, Root.Dpi);
        Root.Draw(context, pixelSize);
    }

    /// <summary>Draws an element as it looks now into a picture that can outlive it.</summary>
    /// <returns>The picture, or <see langword="null"/> when the element has no area to draw.</returns>
    internal UiSnapshot? CreateSnapshot(UiElement element)
    {
        RectangleF bounds = element.Bounds;
        var pixelSize = new SizeI(
            (int)MathF.Ceiling(Root.DipsToPixels(bounds.Width)),
            (int)MathF.Ceiling(Root.DipsToPixels(bounds.Height)));
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
        {
            return null;
        }

        ID2D1Bitmap1 bitmap = DeviceContext.CreateBitmap(
            pixelSize,
            0,
            0,
            new BitmapProperties1(
                new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
                Root.Dpi,
                Root.Dpi,
                BitmapOptions.Target));
        using ID2D1Image previousTarget = DeviceContext.Target;
        Matrix3x2 previousTransform = DeviceContext.Transform;
        try
        {
            DeviceContext.Target = bitmap;
            // Drawing an element moves to where it sits in its parent, so this moves back to the origin.
            DeviceContext.Transform = Matrix3x2.CreateTranslation(-bounds.X, -bounds.Y);
            DeviceContext.BeginDraw();
            DeviceContext.Clear(default(Color4));
            new UiDrawContext(DeviceContext, _brush, _textLayouts, _drawTally, Palette, Root.Dpi).DrawElement(element);
            DeviceContext.EndDraw().CheckError();
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
        finally
        {
            DeviceContext.Target = previousTarget;
            DeviceContext.Transform = previousTransform;
        }

        return new UiSnapshot(bitmap);
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
