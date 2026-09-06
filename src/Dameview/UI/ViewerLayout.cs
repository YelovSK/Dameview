using System.Drawing;

namespace Dameview.UI;

internal readonly record struct ViewerLayout(
    RectangleF Content,
    RectangleF Status,
    RectangleF Toolbar,
    RectangleF Gallery)
{
    internal static ViewerLayout Calculate(
        SizeF size,
        bool showStatus,
        bool showToolbar,
        float toolbarWidthDips = 332.0f,
        bool showGallery = false,
        float galleryWidthDips = 184.0f)
    {
        float margin = UiDesign.WindowMargin;
        RectangleF gallery = RectangleF.Empty;
        float contentWidth = size.Width;
        if (showGallery)
        {
            const float minimumContentWidth = 120.0f;
            float galleryWidth = MathF.Min(
                galleryWidthDips,
                MathF.Max(0.0f, size.Width - 2.0f * margin - UiDesign.PanelGap - minimumContentWidth));
            if (galleryWidth > 0.0f)
            {
                gallery = new RectangleF(
                    size.Width - margin - galleryWidth,
                    margin,
                    galleryWidth,
                    MathF.Max(0.0f, size.Height - 2.0f * margin));
                contentWidth = MathF.Max(0.0f, gallery.X - UiDesign.PanelGap);
            }
        }

        var content = new RectangleF(0.0f, 0.0f, contentWidth, size.Height);
        float availableWidth = MathF.Max(0.0f, contentWidth - (2.0f * margin));
        float availableHeight = MathF.Max(0.0f, size.Height - (2.0f * margin));
        float statusHeight = MathF.Min(UiDesign.StatusHeight, availableHeight);
        RectangleF status = showStatus ? new RectangleF(
            margin,
            MathF.Max(margin, size.Height - statusHeight - margin),
            availableWidth,
            statusHeight) : RectangleF.Empty;

        RectangleF toolbar = RectangleF.Empty;
        if (showToolbar)
        {
            float toolbarWidth = MathF.Min(toolbarWidthDips, availableWidth);
            float availableToolbarHeight = MathF.Max(0.0f, size.Height - 2.0f * margin);
            float toolbarHeight = MathF.Min(UiDesign.ToolbarHeight, availableToolbarHeight);
            toolbar = new RectangleF(
                (contentWidth - toolbarWidth) / 2.0f,
                margin,
                toolbarWidth,
                toolbarHeight);
        }

        return new ViewerLayout(content, status, toolbar, gallery);
    }
}
