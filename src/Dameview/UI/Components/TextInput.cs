using System.Drawing;
using System.Globalization;
using System.Numerics;
using Dameview.Platform;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal sealed class TextInput : UiElement, IDisposable
{
    private const float HorizontalPadding = 10.0f;
    private const float CaretWidth = 1.5f;
    private const float CaretHeight = 20.0f;

    private readonly IDWriteFactory _factory;
    private readonly IDWriteTextFormat _format;
    private IDWriteTextLayout? _textLayout;
    private string _text = string.Empty;
    private float _caretPosition;
    private float _horizontalOffset;
    private float _textWidth;

    internal TextInput(IDWriteFactory factory, string placeholder = "")
    {
        _factory = factory;
        Placeholder = placeholder;
        _format = factory.CreateTextFormat(
            UiTypography.FontFamily,
            FontWeight.Normal,
            FontStyle.Normal,
            UiDesign.BodyFontSize);
        _format.ParagraphAlignment = ParagraphAlignment.Center;
        _format.WordWrapping = WordWrapping.NoWrap;
    }

    internal event Action<string>? TextChanged;

    internal string Text
    {
        get => _text;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_text == value)
            {
                return;
            }

            _text = value;
            CaretIndex = value.Length;
            NotifyTextChanged();
        }
    }

    internal string Placeholder { get; }
    internal int CaretIndex { get; private set; }
    internal override bool IsFocusable => true;
    internal override UiCursor Cursor => UiCursor.Text;

    internal void Clear() => Text = string.Empty;

    internal override bool OnTextInput(string text)
    {
        string insertion = string.Concat(text.Where(character => !char.IsControl(character)));
        if (insertion.Length == 0)
        {
            return false;
        }

        _text = _text.Insert(CaretIndex, insertion);
        CaretIndex += insertion.Length;
        NotifyTextChanged();
        return true;
    }

    internal override bool OnKeyEvent(UiKeyEvent input)
    {
        if (input.Control)
        {
            return false;
        }

        switch (input.Key)
        {
            case UiKey.Backspace:
                DeletePreviousTextElement();
                return true;

            case UiKey.Delete:
                DeleteNextTextElement();
                return true;

            case UiKey.Left:
                SetCaretIndex(GetPreviousTextElementStart());
                return true;

            case UiKey.Right:
                SetCaretIndex(GetNextTextElementStart());
                return true;

            case UiKey.Home:
                SetCaretIndex(0);
                return true;

            case UiKey.End:
                SetCaretIndex(_text.Length);
                return true;

            default:
                return false;
        }
    }

    internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
    {
        if (input.Kind == UiPointerEventKind.Pressed && input.Button == PointerButton.Primary)
        {
            SetCaretIndex(_text.Length);
            return new UiPointerResult(Consumed: true, NeedsRepaint: true);
        }

        return default;
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float width = float.IsFinite(availableSize.Width) ? availableSize.Width : 240.0f;
        return new SizeF(MathF.Max(0.0f, width), 38.0f);
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        var background = new RoundedRectangle(
            new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height),
            UiDesign.ControlCornerRadius,
            UiDesign.ControlCornerRadius);
        context.FillRoundedRectangle(background, context.Palette.Background, 0.72f);
        context.DrawRoundedRectangle(
            background,
            HasVisualState(UiVisualState.Focused)
                ? context.Palette.Accent
                : context.Palette.SurfaceBorder);

        float contentWidth = MathF.Max(0.0f, Bounds.Width - (2.0f * HorizontalPadding));
        if (_text.Length == 0)
        {
            context.DrawText(
                Placeholder,
                _format,
                new Rect(HorizontalPadding, 0.0f, Bounds.Width - HorizontalPadding, Bounds.Height),
                context.Palette.SecondaryText,
                DrawTextOptions.Clip);
        }
        else
        {
            UpdateHorizontalOffset(_caretPosition, _textWidth, contentWidth);
            DrawClippedText(context, contentWidth);
        }

        if (HasVisualState(UiVisualState.Focused))
        {
            float caretPosition = _caretPosition - _horizontalOffset;
            float caretX = HorizontalPadding + Math.Clamp(caretPosition, 0.0f, contentWidth);
            context.FillRoundedRectangle(
                new RoundedRectangle(
                    new RectangleF(
                        caretX,
                        (Bounds.Height - CaretHeight) / 2.0f,
                        CaretWidth,
                        CaretHeight),
                    0.0f,
                    0.0f),
                context.Palette.PrimaryText);
        }
    }

    public void Dispose()
    {
        _textLayout?.Dispose();
        _format.Dispose();
    }

    private void DeletePreviousTextElement()
    {
        int previous = GetPreviousTextElementStart();
        if (previous == CaretIndex)
        {
            return;
        }

        _text = _text.Remove(previous, CaretIndex - previous);
        CaretIndex = previous;
        NotifyTextChanged();
    }

    private void DeleteNextTextElement()
    {
        int next = GetNextTextElementStart();
        if (next == CaretIndex)
        {
            return;
        }

        _text = _text.Remove(CaretIndex, next - CaretIndex);
        NotifyTextChanged();
    }

    private int GetPreviousTextElementStart()
    {
        int previous = 0;
        foreach (int start in StringInfo.ParseCombiningCharacters(_text))
        {
            if (start >= CaretIndex)
            {
                break;
            }

            previous = start;
        }

        return previous;
    }

    private int GetNextTextElementStart()
    {
        foreach (int start in StringInfo.ParseCombiningCharacters(_text))
        {
            if (start > CaretIndex)
            {
                return start;
            }
        }

        return _text.Length;
    }

    private void NotifyTextChanged()
    {
        UpdateTextLayout();
        UpdateCaretPosition();
        TextChanged?.Invoke(_text);
        InvalidateVisual();
    }

    private void SetCaretIndex(int index)
    {
        if (CaretIndex == index)
        {
            return;
        }

        CaretIndex = index;
        UpdateCaretPosition();
        InvalidateVisual();
    }

    private void UpdateTextLayout()
    {
        _textLayout?.Dispose();
        _textLayout = _text.Length == 0
            ? null
            : _factory.CreateTextLayout(
                _text,
                _format,
                100_000.0f,
                38.0f);
        _textWidth = _textLayout?.Metrics.WidthIncludingTrailingWhitespace ?? 0.0f;
    }

    private void UpdateCaretPosition()
    {
        if (CaretIndex == 0)
        {
            _caretPosition = 0.0f;
            return;
        }

        using IDWriteTextLayout prefixLayout = _factory.CreateTextLayout(
            _text[..CaretIndex],
            _format,
            100_000.0f,
            38.0f);
        _caretPosition = prefixLayout.Metrics.WidthIncludingTrailingWhitespace;
    }

    private void UpdateHorizontalOffset(float caretPosition, float textWidth, float contentWidth)
    {
        if (caretPosition < _horizontalOffset)
        {
            _horizontalOffset = caretPosition;
        }
        else if (caretPosition > _horizontalOffset + contentWidth)
        {
            _horizontalOffset = caretPosition - contentWidth;
        }

        _horizontalOffset = Math.Clamp(_horizontalOffset, 0.0f, MathF.Max(0.0f, textWidth - contentWidth));
    }

    private void DrawClippedText(in UiDrawContext context, float contentWidth)
    {
        context.RenderTarget.PushAxisAlignedClip(
            new Rect(HorizontalPadding, 0.0f, HorizontalPadding + contentWidth, Bounds.Height),
            AntialiasMode.Aliased);
        try
        {
            context.DrawTextLayout(
                _textLayout!,
                new Vector2(HorizontalPadding - _horizontalOffset, 0.0f),
                context.Palette.PrimaryText,
                DrawTextOptions.Clip);
        }
        finally
        {
            context.RenderTarget.PopAxisAlignedClip();
        }
    }
}
