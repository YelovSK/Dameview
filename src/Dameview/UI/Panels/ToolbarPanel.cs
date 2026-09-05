using System.Drawing;
using Dameview.Commands;
using Dameview.Platform;
using Dameview.UI.Animation;
using Dameview.UI.Components;
using Dameview.UI.Layout;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Dameview.UI.Panels;

internal sealed class ToolbarPanel : UiElement, IDisposable
{
    private const float WidthWithImage = 332.0f;
    private const float WidthWithoutImage = 104.0f;

    private readonly Button[] _buttons;
    private readonly StackPanel _buttonRow;
    private readonly AnimatedFloat _visibility = new(0.0f, 14.0);
    private readonly UiDesignTokens _design;
    private bool _hasImage;

    internal ToolbarPanel(
        IDWriteFactory directWriteFactory,
        IViewerCommands commands,
        UiDesignTokens design,
        Action showSettings)
    {
        _design = design;
        _buttons =
        [
            new Button(directWriteFactory, "←", commands.ShowPreviousImage, design),
            new Button(directWriteFactory, "→", commands.ShowNextImage, design),
            new Button(directWriteFactory, "Fit", commands.FitImage, design),
            new Button(directWriteFactory, "1:1", commands.ShowActualSize, design),
            new Button(directWriteFactory, "Settings", showSettings, design),
        ];
        _buttonRow = new StackPanel(
            UiOrientation.Horizontal,
            design.SmallSpacing,
            StackPanelDistribution.Equal,
            _buttons);
        AddChild(_buttonRow);

        for (int index = 0; index < _buttons.Length - 1; index++)
        {
            _buttons[index].IsVisible = false;
        }

        Show();
    }

    internal bool HasImage
    {
        get => _hasImage;
        set
        {
            if (_hasImage == value)
            {
                return;
            }

            _hasImage = value;
            for (int index = 0; index < _buttons.Length - 1; index++)
            {
                _buttons[index].IsVisible = value;
            }

            InvalidateLayout();
        }
    }

    internal float WidthDips => HasImage ? WidthWithImage : WidthWithoutImage;
    internal Button SettingsButton => _buttons[^1];
    internal override bool ObservePointerMoves => true;
    internal override float Opacity => _visibility.Current;
    internal override PointF VisualOffset => new(0.0f, (1.0f - _visibility.Current) * Bounds.Height);

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
            MathF.Max(0.0f, _design.ToolbarHeight - 2.0f * padding)));

        return new SizeF(WidthDips, _design.ToolbarHeight);
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        const float padding = 6.0f;
        _buttonRow.Arrange(new RectangleF(
            padding,
            padding,
            MathF.Max(0.0f, finalSize.Width - 2.0f * padding),
            MathF.Max(0.0f, finalSize.Height - 2.0f * padding)));
    }

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        return _visibility.Update(context.ElapsedSeconds);
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        if (Bounds.Width <= 0.0f || Bounds.Height <= 0.0f)
        {
            return;
        }

        var panel = new RoundedRectangle(
            new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height),
            _design.PanelCornerRadius,
            _design.PanelCornerRadius);
        context.FillRoundedRectangle(panel, context.Palette.OverlaySurface);
        context.DrawRoundedRectangle(panel, context.Palette.SurfaceBorder);
    }

    protected override void ObservePointerMove(in UiPointerEvent input)
    {
        bool visible = !HasImage || HasFocusWithin || input.Position.Y >= -28.0f;
        if (_visibility.SetTarget(visible ? 1.0f : 0.0f))
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

    public void Dispose()
    {
        foreach (Button button in _buttons)
        {
            button.Dispose();
        }
    }
}
