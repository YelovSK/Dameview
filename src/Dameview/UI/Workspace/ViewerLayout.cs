using System.Drawing;
using Dameview.UI.Foundation;

namespace Dameview.UI.Workspace;

internal readonly record struct ViewerLayout(
    RectangleF Content,
    RectangleF Status,
    RectangleF Toolbar)
{
    internal static ViewerLayout Calculate(
        SizeF size,
        float statusWidthDips = float.PositiveInfinity,
        float statusHeightDips = UiDesign.StatusHeight,
        float toolbarWidthDips = UiDesign.ToolbarWidth)
    {
        float margin = UiDesign.WindowMargin;
        float contentWidth = size.Width;
        var content = new RectangleF(0.0f, 0.0f, contentWidth, size.Height);
        float availableWidth = MathF.Max(0.0f, contentWidth - (2.0f * margin));
        float availableHeight = MathF.Max(0.0f, size.Height - (2.0f * margin));
        float statusHeight = MathF.Min(statusHeightDips, availableHeight);
        float statusWidth = MathF.Min(statusWidthDips, availableWidth);
        var status = new RectangleF(
            MathF.Max(margin, (contentWidth - statusWidth) / 2.0f),
            MathF.Max(margin, size.Height - statusHeight - margin),
            statusWidth,
            statusHeight);

        float toolbarWidth = MathF.Min(toolbarWidthDips, availableWidth);
        float toolbarHeight = MathF.Min(UiDesign.ToolbarHeight, availableHeight);
        var toolbar = new RectangleF(
            (contentWidth - toolbarWidth) / 2.0f,
            margin,
            toolbarWidth,
            toolbarHeight);

        return new ViewerLayout(content, status, toolbar);
    }
}
