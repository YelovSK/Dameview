using System.Drawing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class StatusPanel : IUiElement, IDisposable
{
    private const float HorizontalPadding = 14.0f;

    private readonly ID2D1SolidColorBrush _backgroundBrush;
    private readonly ID2D1SolidColorBrush _borderBrush;
    private readonly ID2D1SolidColorBrush _primaryTextBrush;
    private readonly ID2D1SolidColorBrush _secondaryTextBrush;
    private readonly ID2D1SolidColorBrush _errorTextBrush;
    private readonly IDWriteTextFormat _fileNameFormat;
    private readonly IDWriteTextFormat _detailsFormat;

    internal StatusPanel(
        ID2D1RenderTarget renderTarget,
        IDWriteFactory directWriteFactory,
        UiTheme theme)
    {
        _backgroundBrush = renderTarget.CreateSolidColorBrush(theme.OverlaySurface);
        _borderBrush = renderTarget.CreateSolidColorBrush(theme.SurfaceBorder);
        _primaryTextBrush = renderTarget.CreateSolidColorBrush(theme.PrimaryText);
        _secondaryTextBrush = renderTarget.CreateSolidColorBrush(theme.SecondaryText);
        _errorTextBrush = renderTarget.CreateSolidColorBrush(theme.ErrorText);
        _fileNameFormat = CreateFormat(directWriteFactory, TextAlignment.Leading);
        _detailsFormat = CreateFormat(directWriteFactory, TextAlignment.Trailing);
    }

    internal ViewerStatus Status { get; set; }

    public void Draw(in UiDrawContext context, SizeF size)
    {
        float width = context.PixelsToDips(size.Width);
        float height = context.PixelsToDips(size.Height);
        if (width <= 0.0f || height <= 0.0f)
        {
            return;
        }

        ID2D1RenderTarget renderTarget = context.RenderTarget;
        var panel = new RoundedRectangle(
            new RectangleF(0.0f, 0.0f, width, height),
            10.0f,
            10.0f);
        renderTarget.FillRoundedRectangle(panel, _backgroundBrush);
        renderTarget.DrawRoundedRectangle(panel, _borderBrush);

        var content = new Rect(
            HorizontalPadding,
            0.0f,
            MathF.Max(0.0f, width - (2.0f * HorizontalPadding)),
            height);

        if (Status.Message is string message)
        {
            renderTarget.DrawText(
                message,
                _fileNameFormat,
                content,
                Status.IsError ? _errorTextBrush : _secondaryTextBrush,
                DrawTextOptions.Clip);
            return;
        }

        float detailsWidth = MathF.Min(220.0f, MathF.Max(100.0f, width * 0.38f));
        float fileNameWidth = MathF.Max(0.0f, content.Width - detailsWidth - HorizontalPadding);

        renderTarget.DrawText(
            Status.FileName,
            _fileNameFormat,
            new Rect(content.X, content.Y, fileNameWidth, content.Height),
            _primaryTextBrush,
            DrawTextOptions.Clip);

        string details = $"{Status.ImageWidth} × {Status.ImageHeight}    {Status.ZoomPercentage:0.#}%";
        renderTarget.DrawText(
            details,
            _detailsFormat,
            new Rect(
                content.X + fileNameWidth + HorizontalPadding,
                content.Y,
                detailsWidth,
                content.Height),
            _secondaryTextBrush,
            DrawTextOptions.Clip);
    }

    public UiPointerResult HandlePointer(in UiPointerEvent input, SizeF size)
    {
        return default;
    }

    public void Dispose()
    {
        _detailsFormat.Dispose();
        _fileNameFormat.Dispose();
        _errorTextBrush.Dispose();
        _secondaryTextBrush.Dispose();
        _primaryTextBrush.Dispose();
        _borderBrush.Dispose();
        _backgroundBrush.Dispose();
    }

    private static IDWriteTextFormat CreateFormat(
        IDWriteFactory factory,
        TextAlignment textAlignment)
    {
        IDWriteTextFormat format = factory.CreateTextFormat(
            "Segoe UI Variable",
            FontWeight.Medium,
            FontStyle.Normal,
            13.0f);
        format.TextAlignment = textAlignment;
        format.ParagraphAlignment = ParagraphAlignment.Center;
        format.WordWrapping = WordWrapping.NoWrap;
        return format;
    }
}

internal readonly record struct ViewerStatus(
    string FileName,
    int ImageWidth,
    int ImageHeight,
    float ZoomPercentage,
    string? Message,
    bool IsError);
