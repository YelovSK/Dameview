using Vortice.DirectWrite;

namespace Dameview.UI.Foundation;

/// <summary>How a piece of text is set. The text layout cache owns the DirectWrite format behind it.</summary>
/// <param name="Ellipsis">Trims text that overflows its box with an ellipsis.</param>
/// <param name="TabStop">The distance between tab stops, or <see langword="null"/> for the default.</param>
internal readonly record struct UiFont(
    float Size,
    FontWeight Weight = FontWeight.Normal,
    TextAlignment Alignment = TextAlignment.Leading,
    ParagraphAlignment VerticalAlignment = ParagraphAlignment.Center,
    WordWrapping Wrapping = WordWrapping.NoWrap,
    bool Ellipsis = false,
    string Family = UiTypography.FontFamily,
    float? TabStop = null);
