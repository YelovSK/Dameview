using System.Drawing;
using Dameview.Commands;
using Dameview.Platform;
using Dameview.UI.Animation;
using Dameview.UI.Components;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Dameview.UI.Panels;

internal sealed class ToolbarPanel : UiElement, IDisposable
{
    private const float WidthWithImage = 332.0f;
    private const float WidthWithoutImage = 104.0f;

    private readonly Button[] _buttons;
    private readonly Button[] _settingsOnly;
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
        _settingsOnly = [_buttons[^1]];
        foreach (Button button in _buttons)
        {
            AddChild(button);
        }

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
        foreach (Button button in VisibleButtons)
        {
            button.Measure(new SizeF(float.PositiveInfinity, _design.ToolbarHeight));
        }

        return new SizeF(WidthDips, _design.ToolbarHeight);
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        Button[] buttons = VisibleButtons;
        float padding = 6.0f;
        float buttonGap = _design.SmallSpacing;
        float totalGaps = buttonGap * (buttons.Length - 1);
        float buttonWidth = MathF.Max(
            0.0f,
            (finalSize.Width - (2.0f * padding) - totalGaps) / buttons.Length);
        for (int index = 0; index < buttons.Length; index++)
        {
            buttons[index].Arrange(new RectangleF(
                padding + (index * (buttonWidth + buttonGap)),
                padding,
                buttonWidth,
                MathF.Max(0.0f, finalSize.Height - (2.0f * padding))));
        }
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

    private Button[] VisibleButtons => HasImage ? _buttons : _settingsOnly;
}
