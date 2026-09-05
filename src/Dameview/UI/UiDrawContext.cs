using System.Drawing;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI;

// Frame-local value. Copies share a UI-thread-owned scratch brush, which is
// fully configured and consumed inside each drawing operation.
internal readonly record struct UiDrawContext
{
    private readonly ID2D1SolidColorBrush _brush;

    internal UiDrawContext(ID2D1RenderTarget renderTarget, ID2D1SolidColorBrush brush, UiTheme palette, float dpi)
    {
        RenderTarget = renderTarget;
        _brush = brush;
        Palette = palette;
        Dpi = dpi;
        Opacity = 1.0f;
    }

    internal ID2D1RenderTarget RenderTarget { get; }
    internal UiTheme Palette { get; }
    internal float Dpi { get; }
    internal float Opacity { get; private init; }

    internal void FillRoundedRectangle(RoundedRectangle rectangle, Color4 color, float opacity = 1.0f)
    {
        RenderTarget.FillRoundedRectangle(rectangle, PrepareBrush(color, opacity));
    }

    internal void DrawRoundedRectangle(RoundedRectangle rectangle, Color4 color, float strokeWidth = 1.0f, float opacity = 1.0f)
    {
        RenderTarget.DrawRoundedRectangle(rectangle, PrepareBrush(color, opacity), strokeWidth);
    }

    internal void DrawText(string text, IDWriteTextFormat format, Rect bounds, Color4 color,
        DrawTextOptions options = DrawTextOptions.None, float opacity = 1.0f)
    {
        RenderTarget.DrawText(text, format, bounds, PrepareBrush(color, opacity), options);
    }

    private ID2D1SolidColorBrush PrepareBrush(Color4 color, float opacity)
    {
        _brush.Color = color;
        _brush.Opacity = Opacity * Math.Clamp(opacity, 0.0f, 1.0f);
        return _brush;
    }

    internal float PixelsToDips(float pixels)
    {
        return UiDpi.PixelsToDips(pixels, Dpi);
    }

    internal RectangleF PixelsToDips(RectangleF rectangle)
    {
        return new RectangleF(
            PixelsToDips(rectangle.X),
            PixelsToDips(rectangle.Y),
            PixelsToDips(rectangle.Width),
            PixelsToDips(rectangle.Height));
    }

    internal void DrawElement(IUiElement element, RectangleF bounds)
    {
        RectangleF dipBounds = PixelsToDips(bounds);
        Matrix3x2 previousTransform = RenderTarget.Transform;
        RenderTarget.Transform = Matrix3x2.CreateTranslation(dipBounds.X, dipBounds.Y)
            * previousTransform;
        RenderTarget.PushAxisAlignedClip(
            new Rect(0.0f, 0.0f, dipBounds.Width, dipBounds.Height),
            AntialiasMode.Aliased);

        try
        {
            element.Draw(this, bounds.Size);
        }
        finally
        {
            RenderTarget.PopAxisAlignedClip();
            RenderTarget.Transform = previousTransform;
        }
    }

    internal UiDrawContext WithOpacity(float opacity)
    {
        return this with { Opacity = Opacity * Math.Clamp(opacity, 0.0f, 1.0f) };
    }
}
