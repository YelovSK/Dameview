using System.Drawing;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class StatusPanel : IUiElement, IDisposable
{
    private const float HorizontalPadding = 14.0f;

    private readonly IDWriteTextFormat _fileNameFormat;
    private readonly IDWriteTextFormat _detailsFormat;

    internal StatusPanel(
        IDWriteFactory directWriteFactory)
    {
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

        var panel = new RoundedRectangle(
            new RectangleF(0.0f, 0.0f, width, height),
            10.0f,
            10.0f);
        context.FillRoundedRectangle(panel, context.Palette.OverlaySurface);
        context.DrawRoundedRectangle(panel, context.Palette.SurfaceBorder);

        var content = new Rect(
            HorizontalPadding,
            0.0f,
            MathF.Max(0.0f, width - (2.0f * HorizontalPadding)),
            height);

        if (Status.Message is string message)
        {
            context.DrawText(
                message,
                _fileNameFormat,
                content,
                Status.IsError ? context.Palette.ErrorText : context.Palette.SecondaryText,
                DrawTextOptions.Clip);
            return;
        }

        float detailsWidth = MathF.Min(220.0f, MathF.Max(100.0f, width * 0.38f));
        float fileNameWidth = MathF.Max(0.0f, content.Width - detailsWidth - HorizontalPadding);

        context.DrawText(
            Status.FileName,
            _fileNameFormat,
            new Rect(content.X, content.Y, fileNameWidth, content.Height),
            context.Palette.PrimaryText,
            DrawTextOptions.Clip);

        string details = $"{Status.ImageWidth} × {Status.ImageHeight}    {Status.ZoomPercentage:0.#}%";
        context.DrawText(
            details,
            _detailsFormat,
            new Rect(
                content.X + fileNameWidth + HorizontalPadding,
                content.Y,
                detailsWidth,
                content.Height),
            context.Palette.SecondaryText,
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
