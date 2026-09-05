using System.Drawing;

namespace Dameview.UI;

internal readonly record struct ViewerLayout(
    RectangleF Content,
    RectangleF Status,
    RectangleF Toolbar)
{
    private const float StatusMarginDips = 12.0f;
    private const float StatusHeightDips = 42.0f;
    private const float ToolbarHeightDips = 46.0f;
    private const float PanelGapDips = 8.0f;

    internal static ViewerLayout Calculate(
        SizeF size,
        float dpi,
        bool showStatus,
        bool showToolbar,
        float toolbarWidthDips = 332.0f)
    {
        var content = new RectangleF(0.0f, 0.0f, size.Width, size.Height);
        float scale = UiDpi.GetScale(dpi);
        float margin = StatusMarginDips * scale;
        float availableWidth = MathF.Max(0.0f, size.Width - (2.0f * margin));
        float availableHeight = MathF.Max(0.0f, size.Height - (2.0f * margin));
        float statusHeight = MathF.Min(StatusHeightDips * scale, availableHeight);
        RectangleF status = showStatus ? new RectangleF(
            margin,
            MathF.Max(margin, size.Height - statusHeight - margin),
            availableWidth,
            statusHeight) : RectangleF.Empty;

        RectangleF toolbar = RectangleF.Empty;
        if (showToolbar)
        {
            float bottom = showStatus ? status.Y - PanelGapDips * scale : size.Height - margin;
            float toolbarWidth = MathF.Min(toolbarWidthDips * scale, availableWidth);
            float availableToolbarHeight = MathF.Max(0.0f, bottom - margin);
            float toolbarHeight = MathF.Min(ToolbarHeightDips * scale, availableToolbarHeight);
            toolbar = new RectangleF(
                (size.Width - toolbarWidth) / 2.0f,
                MathF.Max(margin, bottom - toolbarHeight),
                toolbarWidth,
                toolbarHeight);
        }

        return new ViewerLayout(content, status, toolbar);
    }
}
