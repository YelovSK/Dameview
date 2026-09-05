using System.Drawing;

namespace Dameview.UI;

internal readonly record struct ViewerLayout(
    RectangleF Content,
    RectangleF Status,
    RectangleF Toolbar)
{
    internal static ViewerLayout Calculate(
        SizeF size,
        UiDesignTokens design,
        bool showStatus,
        bool showToolbar,
        float toolbarWidthDips = 332.0f)
    {
        var content = new RectangleF(0.0f, 0.0f, size.Width, size.Height);
        float margin = design.WindowMargin;
        float availableWidth = MathF.Max(0.0f, size.Width - (2.0f * margin));
        float availableHeight = MathF.Max(0.0f, size.Height - (2.0f * margin));
        float statusHeight = MathF.Min(design.StatusHeight, availableHeight);
        RectangleF status = showStatus ? new RectangleF(
            margin,
            MathF.Max(margin, size.Height - statusHeight - margin),
            availableWidth,
            statusHeight) : RectangleF.Empty;

        RectangleF toolbar = RectangleF.Empty;
        if (showToolbar)
        {
            float bottom = showStatus ? status.Y - design.PanelGap : size.Height - margin;
            float toolbarWidth = MathF.Min(toolbarWidthDips, availableWidth);
            float availableToolbarHeight = MathF.Max(0.0f, bottom - margin);
            float toolbarHeight = MathF.Min(design.ToolbarHeight, availableToolbarHeight);
            toolbar = new RectangleF(
                (size.Width - toolbarWidth) / 2.0f,
                MathF.Max(margin, bottom - toolbarHeight),
                toolbarWidth,
                toolbarHeight);
        }

        return new ViewerLayout(content, status, toolbar);
    }
}
