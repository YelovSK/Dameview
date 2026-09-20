using System.Drawing;
using Dameview.Imaging.Decoding;
using Dameview.Rendering;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class EmptyStatePanel : UiElement, IDisposable
{
    private const float ButtonWidth = 104.0f;
    private const float ButtonHeight = 36.0f;

    private readonly IDWriteTextFormat _titleFormat;
    private readonly IDWriteTextFormat _bodyFormat;
    private readonly IDWriteTextFormat _captionFormat;
    private readonly ID2D1Bitmap1 _icon;

    internal EmptyStatePanel(
        IDWriteFactory directWriteFactory,
        ID2D1DeviceContext deviceContext,
        Action openFile,
        Action showSettings)
    {
        _icon = LoadApplicationIcon(deviceContext);
        _titleFormat = CreateCenteredFormat(directWriteFactory, 30.0f, FontWeight.SemiBold);
        _bodyFormat = CreateCenteredFormat(directWriteFactory, 15.0f, FontWeight.Normal);
        _captionFormat = CreateCenteredFormat(directWriteFactory, 12.0f, FontWeight.Medium);
        SettingsButton = new Button(
            directWriteFactory,
            UiTypography.SettingsIcon,
            showSettings,
            fontFamily: UiTypography.IconFontFamily,
            fontSize: 16.0f);
        OpenButton = new Button(directWriteFactory, "Open image", openFile);
        AddChild(OpenButton);
        AddChild(SettingsButton);
    }

    private Button OpenButton { get; }
    private Button SettingsButton { get; }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        OpenButton.Measure(new SizeF(ButtonWidth, ButtonHeight));
        SettingsButton.Measure(new SizeF(ButtonWidth, ButtonHeight));
        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        EmptyStateLayout layout = CalculateLayout(finalSize);
        OpenButton.Arrange(layout.OpenButton);
        SettingsButton.Arrange(layout.SettingsButton);
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
        context.DrawBitmap(
            _icon,
            new Rect(markX + 4.0f, markY + 4.0f, iconSize, iconSize),
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
    }

    public void Dispose()
    {
        OpenButton.Dispose();
        SettingsButton.Dispose();
        _captionFormat.Dispose();
        _bodyFormat.Dispose();
        _titleFormat.Dispose();
        _icon.Dispose();
    }

    private static ID2D1Bitmap1 LoadApplicationIcon(ID2D1DeviceContext deviceContext)
    {
        using Stream stream = typeof(EmptyStatePanel).Assembly.GetManifestResourceStream(
            "Dameview.Assets.dameview.png")
            ?? throw new InvalidOperationException("The embedded application icon could not be found.");
        using var decoder = new ImageDecoder();
        return D2DBitmapFactory.Create(deviceContext, decoder.Decode(stream));
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
        float row = caption.Bottom + 12.0f;
        float left = (size.Width - ((2.0f * ButtonWidth) + UiDesign.SmallSpacing)) / 2.0f;
        return new EmptyStateLayout(
            card,
            caption,
            new RectangleF(left, row, ButtonWidth, ButtonHeight),
            new RectangleF(left + ButtonWidth + UiDesign.SmallSpacing, row, ButtonWidth, ButtonHeight));
    }

    private readonly record struct EmptyStateLayout(
        RectangleF Card,
        RectangleF Caption,
        RectangleF OpenButton,
        RectangleF SettingsButton);
}
