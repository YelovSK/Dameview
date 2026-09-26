using System.Drawing;
using Dameview.UI.Foundation;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal sealed class ShortcutChip : InteractiveControl
{
    internal const float Height = 24.0f;

    private const float HorizontalPadding = 10.0f;
    private const float RemoveWidth = 22.0f;
    private const float MinimumWidth = 34.0f;
    private static readonly UiFont LabelFont = new(12.0f, FontWeight.SemiBold, TextAlignment.Center);
    private static readonly UiFont RemoveFont = new(15.0f, FontWeight.SemiBold, TextAlignment.Center);

    private readonly Action _clicked;
    private readonly Action _removed;
    private bool _removeHovered;
    private string _label;

    internal ShortcutChip(string label, Action clicked, Action removed)
    {
        _label = label;
        _clicked = clicked;
        _removed = removed;
    }

    internal string Label
    {
        get => _label;
        set
        {
            if (_label == value)
            {
                return;
            }

            _label = value;
            InvalidateLayout();
        }
    }

    internal bool CanRemove
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            _removeHovered = false;
            InvalidateLayout();
        }
    }

    internal override bool ObservePointerMoves => true;

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float textWidth = MathF.Ceiling(TextLayouts
            .Get(_label, LabelFont, new SizeF(10_000.0f, Height))
            .Metrics.WidthIncludingTrailingWhitespace);
        float width = textWidth + (2.0f * HorizontalPadding) + (CanRemove ? RemoveWidth : 0.0f);
        return new SizeF(MathF.Max(MinimumWidth, width), Height);
    }

    internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
    {
        if (CanRemove
            && input.Kind == WindowPointerEventKind.Pressed
            && input.Button == PointerButton.Primary
            && GetRemoveBounds().Contains(input.Position))
        {
            _removed();
            return new UiPointerResult(Consumed: true, NeedsRepaint: true);
        }

        return base.OnPointerEvent(input);
    }

    protected override void ObservePointerMove(in WindowPointerEvent input)
    {
        bool hovered = CanRemove && GetRemoveBounds().Contains(input.Position);
        if (_removeHovered == hovered)
        {
            return;
        }

        _removeHovered = hovered;
        InvalidateVisual();
    }

    protected override void ObservePointerLeave()
    {
        if (_removeHovered)
        {
            _removeHovered = false;
            InvalidateVisual();
        }
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        var background = new RoundedRectangle(
            new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height),
            UiDesign.ControlCornerRadius,
            UiDesign.ControlCornerRadius);
        context.FillRoundedRectangle(background, context.Palette.ControlSurface);
        if (HoverAmount > 0.0f)
        {
            context.FillRoundedRectangle(background, context.Palette.ControlHover, HoverAmount);
        }

        if (PressedAmount > 0.0f)
        {
            context.FillRoundedRectangle(background, context.Palette.ControlPressed, PressedAmount);
        }

        float labelWidth = Bounds.Width - (CanRemove ? RemoveWidth : 0.0f);
        context.DrawText(
            _label,
            LabelFont,
            new Rect(0.0f, 0.0f, labelWidth, Bounds.Height),
            context.Palette.PrimaryText,
            DrawTextOptions.Clip);

        if (CanRemove)
        {
            RectangleF remove = GetRemoveBounds();
            Color4 error = context.Palette.ErrorText;
            context.DrawText(
                "×",
                RemoveFont,
                new Rect(remove.X, remove.Y, remove.Width, remove.Height),
                _removeHovered ? error : new Color4(error.R, error.G, error.B, 0.75f));
        }
    }

    protected override void Activate() => _clicked();

    private RectangleF GetRemoveBounds() =>
        new(MathF.Max(0.0f, Bounds.Width - RemoveWidth), 0.0f, RemoveWidth, Bounds.Height);
}
