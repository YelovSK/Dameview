using System.Drawing;
using Dameview.UI.Foundation;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal sealed class ShortcutChip : InteractiveControl, IDisposable
{
    internal const float Height = 24.0f;

    private const float FontSize = 12.0f;
    private const float HorizontalPadding = 10.0f;
    private const float RemoveWidth = 22.0f;
    private const float RemoveFontSize = 15.0f;
    private const float MinimumWidth = 34.0f;

    private readonly IDWriteFactory _factory;
    private readonly IDWriteTextFormat _format;
    private readonly IDWriteTextFormat _removeFormat;
    private readonly Action _clicked;
    private readonly Action _removed;
    private bool _removeHovered;
    private string _label;

    internal ShortcutChip(IDWriteFactory factory, string label, Action clicked, Action removed)
    {
        _factory = factory;
        _label = label;
        _clicked = clicked;
        _removed = removed;
        _format = factory.CreateTextFormat(
            UiTypography.FontFamily,
            FontWeight.SemiBold,
            FontStyle.Normal,
            FontSize);
        _format.TextAlignment = TextAlignment.Center;
        _format.ParagraphAlignment = ParagraphAlignment.Center;
        _format.WordWrapping = WordWrapping.NoWrap;
        _removeFormat = factory.CreateTextFormat(
            UiTypography.FontFamily,
            FontWeight.SemiBold,
            FontStyle.Normal,
            RemoveFontSize);
        _removeFormat.TextAlignment = TextAlignment.Center;
        _removeFormat.ParagraphAlignment = ParagraphAlignment.Center;
        _removeFormat.WordWrapping = WordWrapping.NoWrap;
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
        using IDWriteTextLayout layout = _factory.CreateTextLayout(_label, _format, 10_000.0f, Height);
        float textWidth = MathF.Ceiling(layout.Metrics.WidthIncludingTrailingWhitespace);
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
            _format,
            new Rect(0.0f, 0.0f, labelWidth, Bounds.Height),
            context.Palette.PrimaryText,
            DrawTextOptions.Clip);

        if (CanRemove)
        {
            RectangleF remove = GetRemoveBounds();
            Color4 error = context.Palette.ErrorText;
            context.DrawText(
                "×",
                _removeFormat,
                new Rect(remove.X, remove.Y, remove.Width, remove.Height),
                _removeHovered ? error : new Color4(error.R, error.G, error.B, 0.75f));
        }
    }

    public void Dispose()
    {
        _removeFormat.Dispose();
        _format.Dispose();
    }

    protected override void Activate() => _clicked();

    private RectangleF GetRemoveBounds() =>
        new(MathF.Max(0.0f, Bounds.Width - RemoveWidth), 0.0f, RemoveWidth, Bounds.Height);
}
