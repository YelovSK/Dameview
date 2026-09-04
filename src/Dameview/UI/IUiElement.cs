using System.Drawing;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Dameview.UI;

internal interface IUiElement
{
    public bool Update(in UiUpdateContext context)
    {
        return false;
    }

    public void Draw(in UiDrawContext context, SizeF size);

    public bool HandlePointer(in UiPointerEvent input, SizeF size);
}

internal readonly record struct UiDrawContext(
    ID2D1RenderTarget RenderTarget,
    float Dpi,
    float Opacity = 1.0f)
{
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

internal readonly record struct UiUpdateContext(double ElapsedSeconds);

internal readonly record struct UiPointerEvent(
    UiPointerEventKind Kind,
    PointF Position,
    PointerButton Button = PointerButton.None,
    int WheelDelta = 0)
{
    internal UiPointerEvent ToLocal(RectangleF bounds)
    {
        return this with
        {
            Position = new PointF(
                Position.X - bounds.X,
                Position.Y - bounds.Y),
        };
    }
}

internal enum UiPointerEventKind
{
    Moved,
    Pressed,
    Released,
    DoubleClicked,
    Wheel,
}

internal enum PointerButton
{
    None,
    Primary,
    Secondary,
    Middle,
}
