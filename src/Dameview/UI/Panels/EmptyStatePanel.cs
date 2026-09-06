using System.Drawing;
using Dameview.UI.Components;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class EmptyStatePanel : UiElement, IDisposable
{
    private readonly IDWriteTextFormat _titleFormat;
    private readonly IDWriteTextFormat _bodyFormat;
    private readonly IDWriteTextFormat _captionFormat;
    private readonly ID2D1Bitmap1 _icon;
    private readonly Button _settingsButton;

    internal EmptyStatePanel(
        IDWriteFactory directWriteFactory,
        ID2D1Bitmap1 icon,
        Action showSettings)
    {
        _icon = icon;
        _titleFormat = CreateCenteredFormat(directWriteFactory, 30.0f, FontWeight.SemiBold);
        _bodyFormat = CreateCenteredFormat(directWriteFactory, 15.0f, FontWeight.Normal);
        _captionFormat = CreateCenteredFormat(directWriteFactory, 12.0f, FontWeight.Medium);
        _settingsButton = new Button(
            directWriteFactory,
            UiTypography.SettingsIcon,
            showSettings,
            fontFamily: UiTypography.IconFontFamily,
            fontSize: 16.0f);
        AddChild(_settingsButton);
    }

    internal Button SettingsButton => _settingsButton;

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        _settingsButton.Measure(new SizeF(104.0f, 36.0f));
        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        _settingsButton.Arrange(CalculateLayout(finalSize).SettingsButton);
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        EmptyStateLayout layout = CalculateLayout(Bounds.Size);

        RoundedRectangle card = new(
            layout.Card,
            24.0f,
            24.0f);
        context.FillRoundedRectangle(card, context.Palette.Surface);
        context.DrawRoundedRectangle(card, context.Palette.SurfaceBorder);

        float markSize = 72.0f;
        float markX = (Bounds.Width - markSize) / 2.0f;
        float markY = layout.Card.Y + 32.0f;
        RoundedRectangle mark = new(
            new RectangleF(markX, markY, markSize, markSize),
            18.0f,
            18.0f);
        float iconSize = markSize - 8.0f;
        context.RenderTarget.DrawBitmap(
            _icon,
            new Rect(markX + 4.0f, markY + 4.0f, iconSize, iconSize),
            context.Opacity,
            BitmapInterpolationMode.Linear,
            new Rect(0.0f, 0.0f, _icon.PixelSize.Width, _icon.PixelSize.Height));

        context.DrawText(
            "Dameview",
            _titleFormat,
            new Rect(layout.Card.X + 24.0f, markY + markSize + 24.0f, layout.Card.Width - 48.0f, 44.0f),
            context.Palette.PrimaryText);

        context.DrawText(
            "Drop an image here to open it.",
            _bodyFormat,
            new Rect(layout.Card.X + 24.0f, markY + markSize + 68.0f, layout.Card.Width - 48.0f, 32.0f),
            context.Palette.SecondaryText);

        RoundedRectangle pill = new(
            layout.Caption,
            16.0f,
            16.0f);
        context.DrawRoundedRectangle(pill, context.Palette.SurfaceBorder);
        context.DrawText(
            "Win32  •  Direct2D  •  Native AOT",
            _captionFormat,
            new Rect(layout.Caption.X, layout.Caption.Y, layout.Caption.Width, layout.Caption.Height),
            context.Palette.SecondaryText);

        context.DrawRoundedRectangle(
            new RoundedRectangle(
                layout.SettingsButton,
                UiDesign.ControlCornerRadius,
                UiDesign.ControlCornerRadius),
            context.Palette.SurfaceBorder);
    }

    protected override bool HitTestCore(PointF position) => false;

    public void Dispose()
    {
        _settingsButton.Dispose();
        _captionFormat.Dispose();
        _bodyFormat.Dispose();
        _titleFormat.Dispose();
        _icon.Dispose();
    }

    private static IDWriteTextFormat CreateCenteredFormat(
        IDWriteFactory factory,
        float size,
        FontWeight weight)
    {
        IDWriteTextFormat format = factory.CreateTextFormat(
            UiTypography.FontFamily,
            weight,
            FontStyle.Normal,
            size);

        format.TextAlignment = TextAlignment.Center;
        format.ParagraphAlignment = ParagraphAlignment.Center;
        return format;
    }

    private static EmptyStateLayout CalculateLayout(SizeF size)
    {
        float cardWidth = MathF.Min(560.0f, MathF.Max(280.0f, size.Width - 48.0f));
        float cardHeight = MathF.Min(330.0f, MathF.Max(260.0f, size.Height - 96.0f));
        float cardX = (size.Width - cardWidth) / 2.0f;
        float cardY = (size.Height - cardHeight) / 2.0f;
        var card = new RectangleF(cardX, cardY, cardWidth, cardHeight);
        var caption = new RectangleF((size.Width - 254.0f) / 2.0f, card.Bottom - 100.0f, 254.0f, 32.0f);
        var settingsButton = new RectangleF((size.Width - 104.0f) / 2.0f, caption.Bottom + 12.0f, 104.0f, 36.0f);
        return new EmptyStateLayout(card, caption, settingsButton);
    }

    private readonly record struct EmptyStateLayout(
        RectangleF Card,
        RectangleF Caption,
        RectangleF SettingsButton);
}
