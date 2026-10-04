using System.Drawing;
using Dameview.UI.Foundation;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

/// <summary>The minimize, maximize and close buttons at the end of the title bar.</summary>
internal sealed class WindowButtons : UiElement
{
    private const float ButtonWidthDips = 46.0f;
    internal const float WidthDips = 3.0f * ButtonWidthDips;

    private const string MinimizeIcon = "";
    private const string MaximizeIcon = "";
    private const string RestoreIcon = "";
    private const string CloseIcon = "";

    private readonly WindowButton[] _buttons;
    private readonly WindowButton _maximize;
    private readonly float _height;

    /// <param name="height">The title bar's height, which the buttons fill.</param>
    internal WindowButtons(Action minimize, Action toggleMaximized, Action close, float height)
    {
        _height = height;
        _maximize = new WindowButton(MaximizeIcon, toggleMaximized) { ToolTip = new("Maximize") };
        _buttons =
        [
            new WindowButton(MinimizeIcon, minimize) { ToolTip = new("Minimize") },
            _maximize,
            new WindowButton(CloseIcon, close, closes: true) { ToolTip = new("Close") },
        ];
        foreach (WindowButton button in _buttons)
        {
            AddChild(button);
        }

        HorizontalAlignment = UiAlignment.End;
        VerticalAlignment = UiAlignment.Start;
    }

    internal void SetMaximized(bool maximized)
    {
        _maximize.Icon = maximized ? RestoreIcon : MaximizeIcon;
        _maximize.ToolTip = new(maximized ? "Restore" : "Maximize");
    }

    protected override SizeF MeasureCore(SizeF availableSize) => new(WidthDips, _height);

    protected override void ArrangeCore(SizeF finalSize)
    {
        for (int index = 0; index < _buttons.Length; index++)
        {
            _buttons[index].Arrange(new RectangleF(index * ButtonWidthDips, 0.0f, ButtonWidthDips, finalSize.Height));
        }
    }

    protected override bool HitTestCore(PointF position) => false;

    private sealed class WindowButton(string icon, Action clicked, bool closes = false) : InteractiveControl
    {
        private static readonly UiFont IconFont = new(10.0f, Alignment: TextAlignment.Center, Family: UiTypography.IconFontFamily);
        private static readonly Color4 CloseHoverColor = new(0.77f, 0.17f, 0.11f);

        internal string Icon
        {
            get;
            set
            {
                if (field == value)
                {
                    return;
                }

                field = value;
                InvalidateVisual();
            }
        } = icon;

        // Like the system's caption buttons, these take neither focus nor the hand cursor.
        internal override bool IsFocusable => false;
        internal override bool PreservesFocusOnPointerPress => true;
        internal override WindowCursor Cursor => WindowCursor.Default;

        // Unlike the app's own buttons, these act on release like the system's, so sliding off one
        // calls it off.
        internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
        {
            switch (input.Kind)
            {
                case WindowPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                    return new UiPointerResult(Consumed: true, NeedsRepaint: true, CapturePointer: true);

                case WindowPointerEventKind.Released when HasVisualState(UiVisualState.Pressed):
                    if (new RectangleF(PointF.Empty, Bounds.Size).Contains(input.Position))
                    {
                        Activate();
                    }

                    return new UiPointerResult(Consumed: true, NeedsRepaint: true);

                case WindowPointerEventKind.Cancelled when HasVisualState(UiVisualState.Pressed):
                    return new UiPointerResult(Consumed: true, NeedsRepaint: true);

                default:
                    return default;
            }
        }

        protected override void DrawCore(in UiDrawContext context)
        {
            var bounds = new RoundedRectangle(new RectangleF(PointF.Empty, Bounds.Size), 0.0f, 0.0f);
            float hover = MathF.Max(HoverAmount, PressedAmount);
            if (hover > 0.0f)
            {
                context.FillRoundedRectangle(bounds, closes ? CloseHoverColor : context.Palette.ControlHover, hover);
            }

            if (!closes && PressedAmount > 0.0f)
            {
                context.FillRoundedRectangle(bounds, context.Palette.ControlPressed, PressedAmount);
            }

            Color4 iconColor = closes
                ? Color4.Lerp(context.Palette.PrimaryText, new Color4(1.0f, 1.0f, 1.0f), hover)
                : context.Palette.PrimaryText;
            context.DrawText(
                Icon,
                IconFont,
                new Rect(0.0f, 0.0f, Bounds.Width, Bounds.Height),
                iconColor,
                DrawTextOptions.Clip);
        }

        protected override void Activate() => clicked();
    }
}
