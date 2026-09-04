using System.Drawing;

namespace Dameview.UI;

internal readonly record struct ViewerLayout(
    RectangleF Content,
    RectangleF Status,
    RectangleF Toolbar)
{
    private const float StatusMarginDips = 12.0f;
    private const float StatusHeightDips = 42.0f;
    private const float ToolbarWidthDips = 212.0f;
    private const float ToolbarHeightDips = 46.0f;
    private const float PanelGapDips = 8.0f;

    internal static ViewerLayout Calculate(
        SizeF size,
        float dpi,
        bool showStatus,
        bool showToolbar)
    {
        var content = new RectangleF(0.0f, 0.0f, size.Width, size.Height);
        if (!showStatus)
        {
            return new ViewerLayout(content, RectangleF.Empty, RectangleF.Empty);
        }

        float scale = UiDpi.GetScale(dpi);
        float margin = StatusMarginDips * scale;
        float availableWidth = MathF.Max(0.0f, size.Width - (2.0f * margin));
        float availableHeight = MathF.Max(0.0f, size.Height - (2.0f * margin));
        float statusHeight = MathF.Min(StatusHeightDips * scale, availableHeight);
        var status = new RectangleF(
            margin,
            MathF.Max(margin, size.Height - statusHeight - margin),
            availableWidth,
            statusHeight);

        RectangleF toolbar = RectangleF.Empty;
        if (showToolbar)
        {
            float gap = PanelGapDips * scale;
            float toolbarWidth = MathF.Min(ToolbarWidthDips * scale, availableWidth);
            float availableToolbarHeight = MathF.Max(0.0f, status.Y - margin - gap);
            float toolbarHeight = MathF.Min(ToolbarHeightDips * scale, availableToolbarHeight);
            toolbar = new RectangleF(
                (size.Width - toolbarWidth) / 2.0f,
                MathF.Max(margin, status.Y - gap - toolbarHeight),
                toolbarWidth,
                toolbarHeight);
        }

        return new ViewerLayout(content, status, toolbar);
    }
}
