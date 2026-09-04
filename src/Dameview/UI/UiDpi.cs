namespace Dameview.UI;

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
}
