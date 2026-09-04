using System.Drawing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class EmptyStatePanel : IUiElement, IDisposable
{
    private readonly ID2D1SolidColorBrush _surfaceBrush;
    private readonly ID2D1SolidColorBrush _borderBrush;
    private readonly ID2D1SolidColorBrush _accentBrush;
    private readonly ID2D1SolidColorBrush _primaryTextBrush;
    private readonly ID2D1SolidColorBrush _secondaryTextBrush;
    private readonly IDWriteTextFormat _titleFormat;
    private readonly IDWriteTextFormat _bodyFormat;
    private readonly IDWriteTextFormat _captionFormat;

    internal EmptyStatePanel(
        ID2D1RenderTarget renderTarget,
        IDWriteFactory directWriteFactory,
        UiTheme theme)
    {
        _surfaceBrush = renderTarget.CreateSolidColorBrush(theme.Surface);
        _borderBrush = renderTarget.CreateSolidColorBrush(theme.SurfaceBorder);
        _accentBrush = renderTarget.CreateSolidColorBrush(theme.Accent);
        _primaryTextBrush = renderTarget.CreateSolidColorBrush(theme.PrimaryText);
        _secondaryTextBrush = renderTarget.CreateSolidColorBrush(theme.SecondaryText);

        _titleFormat = CreateCenteredFormat(directWriteFactory, 30.0f, FontWeight.SemiBold);
        _bodyFormat = CreateCenteredFormat(directWriteFactory, 15.0f, FontWeight.Normal);
        _captionFormat = CreateCenteredFormat(directWriteFactory, 12.0f, FontWeight.Medium);
    }

    public void Draw(in UiDrawContext context, SizeF size)
    {
        ID2D1RenderTarget renderTarget = context.RenderTarget;
        float width = context.PixelsToDips(size.Width);
        float height = context.PixelsToDips(size.Height);
        float cardWidth = MathF.Min(560.0f, MathF.Max(280.0f, width - 48.0f));
        float cardHeight = MathF.Min(330.0f, MathF.Max(260.0f, height - 96.0f));
        float cardX = (width - cardWidth) / 2.0f;
        float cardY = (height - cardHeight) / 2.0f;

        RoundedRectangle card = new(
            new RectangleF(cardX, cardY, cardWidth, cardHeight),
            24.0f,
            24.0f);
        renderTarget.FillRoundedRectangle(card, _surfaceBrush);
        renderTarget.DrawRoundedRectangle(card, _borderBrush);

        float markSize = 72.0f;
        float markX = (width - markSize) / 2.0f;
        float markY = cardY + 48.0f;
        RoundedRectangle mark = new(
            new RectangleF(markX, markY, markSize, markSize),
            18.0f,
            18.0f);
        renderTarget.DrawRoundedRectangle(mark, _accentBrush, 2.0f);

        renderTarget.DrawText(
            "Dameview",
            _titleFormat,
            new Rect(cardX + 24.0f, markY + markSize + 24.0f, cardWidth - 48.0f, 44.0f),
            _primaryTextBrush);

        renderTarget.DrawText(
            "Drop an image here to open it.",
            _bodyFormat,
            new Rect(cardX + 24.0f, markY + markSize + 68.0f, cardWidth - 48.0f, 32.0f),
            _secondaryTextBrush);

        float pillWidth = 254.0f;
        float pillHeight = 32.0f;
        float pillX = (width - pillWidth) / 2.0f;
        float pillY = cardY + cardHeight - pillHeight - 28.0f;
        RoundedRectangle pill = new(
            new RectangleF(pillX, pillY, pillWidth, pillHeight),
            16.0f,
            16.0f);
        renderTarget.DrawRoundedRectangle(pill, _borderBrush);
        renderTarget.DrawText(
            "Win32  •  Direct2D  •  Native AOT",
            _captionFormat,
            new Rect(pillX, pillY, pillWidth, pillHeight),
            _secondaryTextBrush);
    }

    public UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size)
    {
        return default;
    }

    public void Dispose()
    {
        _captionFormat.Dispose();
        _bodyFormat.Dispose();
        _titleFormat.Dispose();
        _secondaryTextBrush.Dispose();
        _primaryTextBrush.Dispose();
        _accentBrush.Dispose();
        _borderBrush.Dispose();
        _surfaceBrush.Dispose();
    }

    private static IDWriteTextFormat CreateCenteredFormat(
        IDWriteFactory factory,
        float size,
        FontWeight weight)
    {
        IDWriteTextFormat format = factory.CreateTextFormat(
            "Segoe UI Variable",
            weight,
            FontStyle.Normal,
            size);

        format.TextAlignment = TextAlignment.Center;
        format.ParagraphAlignment = ParagraphAlignment.Center;
        return format;
    }
}
