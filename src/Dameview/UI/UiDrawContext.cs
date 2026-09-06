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
        float inset = strokeWidth / 2.0f;
        RectangleF bounds = rectangle.Rect;
        var innerRectangle = new RoundedRectangle(
            new RectangleF(
                bounds.X + inset,
                bounds.Y + inset,
                MathF.Max(0.0f, bounds.Width - strokeWidth),
                MathF.Max(0.0f, bounds.Height - strokeWidth)),
            MathF.Max(0.0f, rectangle.RadiusX - inset),
            MathF.Max(0.0f, rectangle.RadiusY - inset));
        RenderTarget.DrawRoundedRectangle(innerRectangle, PrepareBrush(color, opacity), strokeWidth);
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

    internal float PixelsToDips(float pixels) => UiDpi.PixelsToDips(pixels, Dpi);

    internal void DrawElement(UiElement element)
    {
        RectangleF bounds = element.Bounds;
        PointF offset = element.VisualOffset;
        Matrix3x2 previousTransform = RenderTarget.Transform;
        RenderTarget.Transform = Matrix3x2.CreateTranslation(bounds.X + offset.X, bounds.Y + offset.Y)
            * previousTransform;
        RenderTarget.PushAxisAlignedClip(
            new Rect(0.0f, 0.0f, bounds.Width, bounds.Height),
            AntialiasMode.Aliased);

        try
        {
            UiDrawContext elementContext = WithOpacity(element.Opacity);
            element.Draw(elementContext);
            foreach (UiElement child in element.Children)
            {
                if (child.IsVisible)
                {
                    elementContext.DrawElement(child);
                }
            }
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
