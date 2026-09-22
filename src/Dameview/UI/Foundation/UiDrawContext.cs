using System.Drawing;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Foundation;

// Frame-local value. Copies share a UI-thread-owned scratch brush, which is
// fully configured and consumed inside each drawing operation.
internal readonly record struct UiDrawContext
{
    private readonly ID2D1SolidColorBrush _brush;
    private readonly UiTextLayoutCache _textLayouts;
    private readonly UiDrawTally _tally;

    internal UiDrawContext(
        ID2D1RenderTarget renderTarget,
        ID2D1SolidColorBrush brush,
        UiTextLayoutCache textLayouts,
        UiDrawTally tally,
        UiTheme palette,
        float dpi)
    {
        RenderTarget = renderTarget;
        _brush = brush;
        _textLayouts = textLayouts;
        _tally = tally;
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

    internal void DrawRoundedRectangle(RoundedRectangle rectangle, Color4 color, float strokeWidthPixels = 1.0f, float opacity = 1.0f)
    {
        float strokeWidth = PixelsToDips(strokeWidthPixels);
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
        IDWriteTextLayout layout = _textLayouts.Get(
            text, format, new SizeF(bounds.Width, bounds.Height));
        RenderTarget.DrawTextLayout(
            new Vector2(bounds.Left, bounds.Top),
            layout,
            PrepareBrush(color, opacity),
            options);
    }

    /// <summary>Clips everything drawn until the matching <see cref="PopClip"/> to these bounds.</summary>
    internal void PushClip(RectangleF bounds)
    {
        RenderTarget.PushAxisAlignedClip(
            new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            AntialiasMode.Aliased);
    }

    internal void PopClip() => RenderTarget.PopAxisAlignedClip();

    internal void DrawBitmap(
        ID2D1Bitmap bitmap,
        Rect destination,
        Rect source,
        BitmapInterpolationMode interpolationMode = BitmapInterpolationMode.Linear,
        float opacity = 1.0f)
    {
        _tally.Operations++;
        RenderTarget.DrawBitmap(
            bitmap,
            destination,
            Opacity * Math.Clamp(opacity, 0.0f, 1.0f),
            interpolationMode,
            source);
    }

    /// <summary>Draws a whole bitmap, scaled to fit and centered within an area.</summary>
    internal void DrawBitmapFitted(ID2D1Bitmap bitmap, RectangleF area)
    {
        float scale = MathF.Min(
            area.Width / bitmap.PixelSize.Width,
            area.Height / bitmap.PixelSize.Height);
        float width = bitmap.PixelSize.Width * scale;
        float height = bitmap.PixelSize.Height * scale;
        DrawBitmap(
            bitmap,
            new Rect(
                area.X + (area.Width - width) / 2.0f,
                area.Y + (area.Height - height) / 2.0f,
                width,
                height),
            new Rect(0.0f, 0.0f, bitmap.PixelSize.Width, bitmap.PixelSize.Height));
    }

    /// <summary>Counts a draw an element issued against <see cref="RenderTarget"/> itself.</summary>
    internal void CountOperation() => _tally.Operations++;

    internal void DrawTextLayout(IDWriteTextLayout layout, Vector2 origin, Color4 color,
        DrawTextOptions options = DrawTextOptions.None, float opacity = 1.0f)
    {
        RenderTarget.DrawTextLayout(origin, layout, PrepareBrush(color, opacity), options);
    }

    private ID2D1SolidColorBrush PrepareBrush(Color4 color, float opacity)
    {
        _tally.Operations++;
        _brush.Color = color;
        _brush.Opacity = Opacity * Math.Clamp(opacity, 0.0f, 1.0f);
        return _brush;
    }

    internal float PixelsToDips(float pixels) => UiDpi.PixelsToDips(pixels, Dpi);

    internal void DrawElement(UiElement element)
    {
        _tally.Elements++;
        RectangleF bounds = element.Bounds;
        PointF offset = element.VisualOffset;
        Matrix3x2 previousTransform = RenderTarget.Transform;
        var transform = Matrix3x2.CreateTranslation(bounds.X + offset.X, bounds.Y + offset.Y);
        float scale = element.VisualScale;
        if (scale != 1.0f)
        {
            transform = Matrix3x2.CreateScale(scale, new Vector2(bounds.Width / 2.0f, bounds.Height / 2.0f))
                * transform;
        }

        RenderTarget.Transform = transform * previousTransform;
        PushClip(new RectangleF(0.0f, 0.0f, bounds.Width, bounds.Height));

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
            PopClip();
            RenderTarget.Transform = previousTransform;
        }
    }

    internal UiDrawContext WithOpacity(float opacity)
    {
        return this with { Opacity = Opacity * Math.Clamp(opacity, 0.0f, 1.0f) };
    }
}
