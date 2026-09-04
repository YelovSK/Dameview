using System.Drawing;

namespace Dameview.UI;

internal readonly record struct ViewerLayout(
    RectangleF Content,
    RectangleF Status)
{
    private const float StatusMarginDips = 12.0f;
    private const float StatusHeightDips = 42.0f;

    internal static ViewerLayout Calculate(SizeF size, float dpi, bool showStatus)
    {
        var content = new RectangleF(0.0f, 0.0f, size.Width, size.Height);
        if (!showStatus)
        {
            return new ViewerLayout(content, RectangleF.Empty);
        }

        float scale = dpi / 96.0f;
        float margin = StatusMarginDips * scale;
        float availableWidth = MathF.Max(0.0f, size.Width - (2.0f * margin));
        float availableHeight = MathF.Max(0.0f, size.Height - (2.0f * margin));
        float statusHeight = MathF.Min(StatusHeightDips * scale, availableHeight);
        var status = new RectangleF(
            margin,
            MathF.Max(margin, size.Height - statusHeight - margin),
            availableWidth,
            statusHeight);

        return new ViewerLayout(content, status);
    }
}
