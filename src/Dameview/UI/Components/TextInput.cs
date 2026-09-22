using System.Drawing;
using System.Globalization;
using System.Numerics;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;
using Dameview.Win32;
using Dameview.Win32.Input;
using SharpGen.Runtime;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal sealed class TextInput : UiElement
{
    private const float HorizontalPadding = 10.0f;
    private const float CaretWidth = 1.5f;
    private const float CaretHeight = 20.0f;
    private const float SelectionOpacity = 0.35f;
    private const double CaretHeadResponse = 80.0;
    private const double CaretTailResponse = 25.0;
    private static readonly UiFont TextFont = new(UiDesign.BodyFontSize);

    // Both edges of the drawn caret glide toward the layout position of CaretIndex. The tail
    // lags behind, so the caret stretches while it moves fast and shrinks back as it settles.
    private readonly AnimatedFloat _caretHead = new(0.0f, CaretHeadResponse, completionDistance: 0.05f);
    private readonly AnimatedFloat _caretTail = new(0.0f, CaretTailResponse, completionDistance: 0.05f);
    // Only a caret that stayed on screen glides; one that just appeared jumps into place.
    private bool _caretGlides;
    private string _text = string.Empty;
    // The fixed end of the selection; the caret is the end that moves.
    private int _anchor;
    private float _horizontalOffset;

    internal TextInput(string placeholder = "")
    {
        Placeholder = placeholder;
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
            _anchor = CaretIndex = value.Length;
            // Replaced wholesale, e.g. reset while hidden, so there is no edit to animate.
            _caretGlides = false;
            NotifyTextChanged();
        }
    }

    internal string Placeholder { get; }
    internal int CaretIndex { get; private set; }
    internal int SelectionStart => Math.Min(_anchor, CaretIndex);
    internal int SelectionEnd => Math.Max(_anchor, CaretIndex);
    internal string SelectedText => _text[SelectionStart..SelectionEnd];
    internal override bool IsFocusable => true;
    internal override WindowCursor Cursor => WindowCursor.Text;

    private bool HasSelection => _anchor != CaretIndex;
    private IDWriteTextLayout? TextLayout => _text.Length == 0
        ? null
        : TextLayouts.Get(_text, TextFont, new SizeF(100_000.0f, 38.0f));

    internal void Clear() => Text = string.Empty;

    internal override bool OnTextInput(string text) => Insert(text);

    internal override bool OnKeyEvent(WindowKeyEvent input)
    {
        switch (input.Key)
        {
            case WindowKey.Left or WindowKey.Right:
                int direction = input.Key == WindowKey.Left ? -1 : 1;
                int target = HasSelection && !input.Shift && !input.Control
                    ? (direction < 0 ? SelectionStart : SelectionEnd)
                    : FindBoundary(direction, input.Control);
                MoveCaret(target, input.Shift);
                return true;

            case WindowKey.Home:
                MoveCaret(0, input.Shift);
                return true;

            case WindowKey.End:
                MoveCaret(_text.Length, input.Shift);
                return true;

            case WindowKey.Backspace or WindowKey.Delete:
                if (!HasSelection)
                {
                    _anchor = FindBoundary(input.Key == WindowKey.Backspace ? -1 : 1, input.Control);
                }

                ReplaceSelection(string.Empty);
                return true;

            case WindowKey.A when input.Control:
                Select(0, _text.Length);
                return true;

            case WindowKey.C when input.Control && HasSelection:
                _ = Win32Clipboard.TrySetText(SelectedText);
                return true;

            case WindowKey.X when input.Control && HasSelection:
                if (Win32Clipboard.TrySetText(SelectedText))
                {
                    ReplaceSelection(string.Empty);
                }

                return true;

            case WindowKey.V when input.Control:
                if (Win32Clipboard.TryGetText() is { } pasted)
                {
                    Insert(pasted);
                }

                return true;

            default:
                return false;
        }
    }

    internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
    {
        switch (input.Kind)
        {
            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                MoveCaret(HitTest(input.Position.X).Caret, extend: false);
                return new UiPointerResult(Consumed: true, CapturePointer: true);

            // Pressed is only set while this input holds the pointer capture, i.e. during a drag.
            case WindowPointerEventKind.Moved when HasVisualState(UiVisualState.Pressed):
                MoveCaret(HitTest(input.Position.X).Caret, extend: true);
                return new UiPointerResult(Consumed: true);

            case WindowPointerEventKind.DoubleClicked when input.Button == PointerButton.Primary:
                SelectWordAt(HitTest(input.Position.X).Character);
                return new UiPointerResult(Consumed: true);

            default:
                return default;
        }
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float width = float.IsFinite(availableSize.Width) ? availableSize.Width : 240.0f;
        return new SizeF(MathF.Max(0.0f, width), 38.0f);
    }

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        float target = GetCaretPosition(CaretIndex);
        bool headMoving = MoveCaretEdge(_caretHead, target, context);
        bool tailMoving = MoveCaretEdge(_caretTail, target, context);
        _caretGlides = HasVisualState(UiVisualState.Focused);
        return headMoving || tailMoving;
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        var background = new RoundedRectangle(
            new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height),
            UiDesign.ControlCornerRadius,
            UiDesign.ControlCornerRadius);
        bool focused = HasVisualState(UiVisualState.Focused);
        context.FillRoundedRectangle(background, context.Palette.ControlSurface);
        context.DrawRoundedRectangle(
            background,
            focused ? context.Palette.Accent : context.Palette.SurfaceBorder);

        float contentWidth = MathF.Max(0.0f, Bounds.Width - (2.0f * HorizontalPadding));
        float caretPosition = _caretHead.Current;
        float caretTop = (Bounds.Height - CaretHeight) / 2.0f;
        if (_text.Length == 0)
        {
            context.DrawText(
                Placeholder,
                TextFont,
                new Rect(HorizontalPadding, 0.0f, Bounds.Width - HorizontalPadding, Bounds.Height),
                context.Palette.SecondaryText,
                DrawTextOptions.Clip);
        }
        else
        {
            UpdateHorizontalOffset(caretPosition, contentWidth);
            context.PushClip(new RectangleF(HorizontalPadding, 0.0f, contentWidth, Bounds.Height));
            try
            {
                float textLeft = HorizontalPadding - _horizontalOffset;
                if (focused && HasSelection)
                {
                    // Spans to the drawn caret rather than CaretIndex, so the highlight grows with the glide.
                    float anchorPosition = GetCaretPosition(_anchor);
                    float selectionLeft = MathF.Min(anchorPosition, caretPosition);
                    float selectionRight = MathF.Max(anchorPosition, caretPosition);
                    context.FillRoundedRectangle(
                        new RoundedRectangle(
                            new RectangleF(
                                textLeft + selectionLeft,
                                caretTop,
                                selectionRight - selectionLeft,
                                CaretHeight),
                            0.0f,
                            0.0f),
                        context.Palette.Accent,
                        SelectionOpacity);
                }

                context.DrawTextLayout(
                    TextLayout!,
                    new Vector2(textLeft, 0.0f),
                    context.Palette.PrimaryText,
                    DrawTextOptions.Clip);
            }
            finally
            {
                context.PopClip();
            }
        }

        if (focused)
        {
            float headX = HorizontalPadding + Math.Clamp(caretPosition - _horizontalOffset, 0.0f, contentWidth);
            float tailX = HorizontalPadding + Math.Clamp(_caretTail.Current - _horizontalOffset, 0.0f, contentWidth);
            float caretLeft = MathF.Min(headX, tailX);
            context.FillRoundedRectangle(
                new RoundedRectangle(
                    new RectangleF(caretLeft, caretTop, MathF.Abs(headX - tailX) + CaretWidth, CaretHeight),
                    0.0f,
                    0.0f),
                context.Palette.PrimaryText);
        }
    }

    private bool MoveCaretEdge(AnimatedFloat edge, float target, in UiUpdateContext context)
    {
        if (_caretGlides)
        {
            edge.SetTarget(target);
        }
        else
        {
            edge.SetValue(target);
        }

        return edge.Update(context);
    }

    private bool Insert(string text)
    {
        string insertion = string.Concat(text.Where(character => !char.IsControl(character)));
        if (insertion.Length == 0)
        {
            return false;
        }

        ReplaceSelection(insertion);
        return true;
    }

    private void ReplaceSelection(string replacement)
    {
        if (!HasSelection && replacement.Length == 0)
        {
            return;
        }

        int start = SelectionStart;
        _text = string.Concat(_text.AsSpan(0, start), replacement, _text.AsSpan(SelectionEnd));
        _anchor = CaretIndex = start + replacement.Length;
        NotifyTextChanged();
    }

    private void MoveCaret(int index, bool extend) => Select(extend ? _anchor : index, index);

    private void Select(int anchor, int caret)
    {
        _anchor = anchor;
        CaretIndex = caret;
        InvalidateVisual();
    }

    private void SelectWordAt(int index)
    {
        if (_text.Length == 0)
        {
            return;
        }

        index = Math.Min(index, _text.Length - 1);
        CharacterClass run = GetCharacterClass(_text[index]);
        Select(Scan(index, -1, run), Scan(index, 1, run));
    }

    private int FindBoundary(int direction, bool word)
    {
        if (word)
        {
            int index = Scan(CaretIndex, direction, CharacterClass.Whitespace);
            return GetAdjacentClass(index, direction) is { } run ? Scan(index, direction, run) : index;
        }

        return direction < 0 ? GetPreviousTextElementStart() : GetNextTextElementStart();
    }

    private int Scan(int index, int direction, CharacterClass run)
    {
        while (GetAdjacentClass(index, direction) == run)
        {
            index += direction;
        }

        return index;
    }

    private CharacterClass? GetAdjacentClass(int index, int direction)
    {
        int next = direction < 0 ? index - 1 : index;
        return next >= 0 && next < _text.Length ? GetCharacterClass(_text[next]) : null;
    }

    // Surrogates and combining marks count as word characters, so a word boundary never splits a text element.
    private static CharacterClass GetCharacterClass(char character) =>
        char.IsWhiteSpace(character) ? CharacterClass.Whitespace
        : char.IsPunctuation(character) || char.IsSymbol(character) ? CharacterClass.Punctuation
        : CharacterClass.Word;

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

    private (int Character, int Caret) HitTest(float x)
    {
        if (TextLayout is not { } layout)
        {
            return (0, 0);
        }

        layout.HitTestPoint(
            x - HorizontalPadding + _horizontalOffset,
            Bounds.Height / 2.0f,
            out RawBool isTrailingHit,
            out _,
            out HitTestMetrics metrics);
        int character = (int)metrics.TextPosition;
        return (character, isTrailingHit ? character + (int)metrics.Length : character);
    }

    private float GetCaretPosition(int index)
    {
        if (index == 0 || TextLayout is not { } layout)
        {
            return 0.0f;
        }

        layout.HitTestTextPosition((uint)(index - 1), true, out float x, out _, out _);
        return x;
    }

    private void NotifyTextChanged()
    {
        TextChanged?.Invoke(_text);
        InvalidateVisual();
    }

    private void UpdateHorizontalOffset(float caretPosition, float contentWidth)
    {
        if (caretPosition < _horizontalOffset)
        {
            _horizontalOffset = caretPosition;
        }
        else if (caretPosition > _horizontalOffset + contentWidth)
        {
            _horizontalOffset = caretPosition - contentWidth;
        }

        float textWidth = TextLayout?.Metrics.WidthIncludingTrailingWhitespace ?? 0.0f;
        _horizontalOffset = Math.Clamp(_horizontalOffset, 0.0f, MathF.Max(0.0f, textWidth - contentWidth));
    }

    private enum CharacterClass
    {
        Whitespace,
        Punctuation,
        Word,
    }
}
