using System.Drawing;
using Dameview.Commands;
using Dameview.UI.Animation;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.Viewing;
using Dameview.Win32.Input;
using Vortice.Direct2D1;

namespace Dameview.UI.Panels;

internal sealed class ToolbarPanel : UiElement
{
    internal const float WidthDips = UiDesign.ToolbarWidth;

    private readonly StackPanel _buttonRow;

    internal ToolbarPanel(
        IViewerCommands commands,
        ViewerPane pane,
        Action showSettings)
    {
        Button[] buttons =
        [
            new Button("←", () => commands.ShowPreviousImage(pane)),
            new Button("→", () => commands.ShowNextImage(pane)),
            new Button("Fit", () => commands.FitImage(pane)),
            new Button("1:1", () => commands.ShowActualSize(pane)),
            new Button("Split →", () => commands.SplitRight(pane)),
            new Button("Split ↓", () => commands.SplitDown(pane)),
            new Button(
                UiTypography.SettingsIcon,
                showSettings,
                fontFamily: UiTypography.IconFontFamily,
                fontSize: 16.0f),
        ];
        _buttonRow = new StackPanel(
            UiOrientation.Horizontal,
            UiDesign.SmallSpacing,
            StackPanelDistribution.Equal,
            buttons);
        AddChild(_buttonRow);
        Transition = new UiTransition(Fade: true, HiddenOffset: new PointF(0.0f, -UiDesign.ToolbarHeight), Response: 14.0);
        IsPresent = false;
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        const float padding = 6.0f;
        _buttonRow.Measure(new SizeF(
            MathF.Max(0.0f, availableSize.Width - 2.0f * padding),
            MathF.Max(0.0f, UiDesign.ToolbarHeight - 2.0f * padding)));

        return new SizeF(WidthDips, UiDesign.ToolbarHeight);
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        const float padding = 6.0f;
        RectangleF content = GetContentBounds(finalSize);
        _buttonRow.Arrange(new RectangleF(
            content.X + padding,
            content.Y + padding,
            MathF.Max(0.0f, content.Width - 2.0f * padding),
            MathF.Max(0.0f, content.Height - 2.0f * padding)));
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        if (Bounds.Width <= 0.0f || Bounds.Height <= 0.0f)
        {
            return;
        }

        RectangleF content = GetContentBounds(Bounds.Size);
        var panel = new RoundedRectangle(
            content,
            UiDesign.PanelCornerRadius,
            UiDesign.PanelCornerRadius);
        context.FillRoundedRectangle(panel, context.Palette.OverlaySurface);
        context.DrawRoundedRectangle(panel, context.Palette.SurfaceBorder);
    }

    private static RectangleF GetContentBounds(SizeF availableSize)
    {
        float width = MathF.Min(WidthDips, MathF.Max(0.0f, availableSize.Width));
        return new RectangleF(
            (availableSize.Width - width) / 2.0f,
            0.0f,
            width,
            availableSize.Height);
    }
}
