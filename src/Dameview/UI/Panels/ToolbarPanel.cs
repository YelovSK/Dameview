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
    private readonly AnimatedFloat _visibility;
    private bool _pointerNear;

    internal ToolbarPanel(
        IViewerCommands commands,
        ViewerPane pane,
        Action showSettings)
    {
        _visibility = Animate(0.0f, 14.0);
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
    }

    internal override float Opacity => _visibility.Current;
    internal override PointF VisualOffset => new(0.0f, (_visibility.Current - 1.0f) * Bounds.Height);
    // Stays in the tree while hidden so it can notice the pointer coming near, but only takes clicks while shown.
    internal override bool IsHitTestVisible => _visibility.Target > 0.0f;

    internal void Show()
    {
        if (_visibility.SetTarget(1.0f))
        {
            InvalidateVisual();
        }
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

    internal void SetPointerNear(bool pointerNear)
    {
        _pointerNear = pointerNear;
        if (_visibility.SetTarget(HasFocusWithin || _pointerNear ? 1.0f : 0.0f))
        {
            InvalidateVisual();
        }
    }

    protected override void OnFocusWithinChanged()
    {
        if (HasFocusWithin)
        {
            Show();
        }
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
