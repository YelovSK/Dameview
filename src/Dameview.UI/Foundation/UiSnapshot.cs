using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Dameview.UI.Foundation;

/// <summary>Shows a picture of an element as it last looked, so it can animate away after the element is gone.</summary>
internal sealed class UiSnapshot(ID2D1Bitmap1 bitmap) : UiElement, IDisposable
{
    /// <summary>Whether the picture stays against the right or bottom edge when this view is smaller than it.</summary>
    internal bool AlignToEnd { get; set; }

    internal override bool IsHitTestVisible => false;

    public void Dispose() => bitmap.Dispose();

    protected override void DrawCore(in UiDrawContext context)
    {
        // Drawn at the bitmap's own size on whole pixels, so it matches what it replaced exactly.
        Size size = bitmap.Size;
        float x = AlignToEnd ? UiDpi.SnapToPixel(Bounds.Width - size.Width, context.Dpi) : 0.0f;
        float y = AlignToEnd ? UiDpi.SnapToPixel(Bounds.Height - size.Height, context.Dpi) : 0.0f;
        context.DrawBitmap(bitmap, new Rect(x, y, size.Width, size.Height));
    }
}
