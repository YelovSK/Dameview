using System.Drawing;

namespace Dameview.UI.Foundation;

internal static class UiDpi
{
    internal const float Default = 96.0f;

    internal static float GetScale(float dpi)
    {
        return dpi / Default;
    }

    internal static float DipsToPixels(float dips, float dpi)
    {
        return dips * GetScale(dpi);
    }

    internal static float PixelsToDips(float pixels, float dpi)
    {
        return pixels / GetScale(dpi);
    }

    internal static RectangleF PixelsToDips(Rectangle pixels, float dpi) => RectangleF.FromLTRB(
        PixelsToDips(pixels.Left, dpi),
        PixelsToDips(pixels.Top, dpi),
        PixelsToDips(pixels.Right, dpi),
        PixelsToDips(pixels.Bottom, dpi));

    internal static float SnapToPixel(float dips, float dpi)
    {
        return PixelsToDips(MathF.Round(DipsToPixels(dips, dpi)), dpi);
    }
}
