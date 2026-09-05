using Dameview.Platform;
using System.Drawing;
using Dameview.UI.Animation;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal sealed class Button : UiElement, IDisposable
{
    private readonly Action _clicked;
    private readonly IDWriteTextFormat _textFormat;
    private readonly AnimatedFloat _hoverAmount;
    private readonly AnimatedFloat _pressedAmount;
    private readonly UiDesignTokens _design;

    internal Button(
        IDWriteFactory directWriteFactory,
        string label,
        Action clicked,
        UiDesignTokens? design = null)
    {
        _design = design ?? UiDesignTokens.Default;
        Label = label;
        _clicked = clicked;
        _hoverAmount = new AnimatedFloat(0.0f, _design.HoverResponse);
        _pressedAmount = new AnimatedFloat(0.0f, _design.PressedResponse);
        _textFormat = directWriteFactory.CreateTextFormat(
            "Segoe UI Variable",
            FontWeight.SemiBold,
            FontStyle.Normal,
            _design.BodyFontSize);
        _textFormat.TextAlignment = TextAlignment.Center;
        _textFormat.ParagraphAlignment = ParagraphAlignment.Center;
        _textFormat.WordWrapping = WordWrapping.NoWrap;
    }

    internal string Label { get; set; }
    internal bool IsSelected
    {
        get => HasVisualState(UiVisualState.Selected);
        set => SetVisualState(UiVisualState.Selected, value);
    }

    internal override bool IsFocusable => true;

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        bool continues = _hoverAmount.Update(context.ElapsedSeconds);
        return _pressedAmount.Update(context.ElapsedSeconds) || continues;
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        float width = Bounds.Width;
        float height = Bounds.Height;
        var bounds = new RectangleF(0.0f, 0.0f, width, height);
        var background = new RoundedRectangle(
            bounds,
            _design.ControlCornerRadius,
            _design.ControlCornerRadius);

        if (IsSelected)
        {
            context.FillRoundedRectangle(background, context.Palette.Accent, 0.22f);
        }

        if (_hoverAmount.Current > 0.0f)
        {
            context.FillRoundedRectangle(background, context.Palette.ControlHover, _hoverAmount.Current);
        }

        if (_pressedAmount.Current > 0.0f)
        {
            context.FillRoundedRectangle(background, context.Palette.ControlPressed, _pressedAmount.Current);
        }

        context.DrawText(
            Label,
            _textFormat,
            new Rect(0.0f, 0.0f, width, height),
            context.Palette.PrimaryText,
            DrawTextOptions.Clip);

        if (HasVisualState(UiVisualState.Focused))
        {
            context.DrawRoundedRectangle(new RoundedRectangle(
                new RectangleF(2, 2, MathF.Max(0, width - 4), MathF.Max(0, height - 4)),
                6, 6), context.Palette.Accent, 2);
        }
    }

    internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
    {
        bool isInside = new RectangleF(PointF.Empty, Bounds.Size).Contains(input.Position);
        switch (input.Kind)
        {
            case UiPointerEventKind.Pressed
                when input.Button == PointerButton.Primary && isInside:
                return new UiPointerResult(Consumed: true, NeedsRepaint: true, CapturePointer: true);

            case UiPointerEventKind.Released when HasVisualState(UiVisualState.Pressed):
                if (isInside)
                {
                    _clicked();
                }

                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case UiPointerEventKind.Cancelled when HasVisualState(UiVisualState.Pressed):
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            case UiPointerEventKind.DoubleClicked
                when input.Button == PointerButton.Primary && isInside:
                _clicked();
                return new UiPointerResult(Consumed: true, NeedsRepaint: true);

            default:
                return default;
        }
    }

    internal override bool OnKeyEvent(UiKeyEvent input)
    {
        if (input.Key is not (UiKey.Space or UiKey.Enter))
        {
            return false;
        }

        _clicked();
        return true;
    }

    public void Dispose()
    {
        _textFormat.Dispose();
    }

    protected override void OnVisualStateChanged()
    {
        _hoverAmount.SetTarget(HasVisualState(UiVisualState.Hovered) ? 1.0f : 0.0f);
        _pressedAmount.SetTarget(
            HasVisualState(UiVisualState.Pressed) && HasVisualState(UiVisualState.Hovered)
                ? 1.0f
                : 0.0f);
    }
}
