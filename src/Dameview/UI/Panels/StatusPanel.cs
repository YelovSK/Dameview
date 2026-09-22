using System.Drawing;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class StatusPanel : UiElement
{
    internal const float HeightDips = 32.0f;

    private const float HorizontalPadding = 12.0f;
    private const float TextGap = 12.0f;
    private const float MaximumWidth = 720.0f;
    private static readonly UiFont TextFont = new(13.0f, FontWeight.Medium);

    private string _fileName = string.Empty;
    private string _details = string.Empty;
    private float _fileNameWidth;
    private float _detailsWidth;
    private float _detailsX;

    internal StatusPanel()
    {
        Transition = new UiTransition(Fade: true, HiddenOffset: new PointF(0.0f, 8.0f), Response: 14.0);
        IsPresent = false;
    }

    private ViewerStatus Status { get; set; }
    internal bool HasMessage => Status.Message is not null;

    internal void SetStatus(ViewerStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        (_fileName, _details) = GetText(status);
        InvalidateLayout();
    }

    internal override bool IsHitTestVisible => false;

    internal static string FormatFileSize(long sizeBytes)
    {
        const long kiloByte = 1024;
        const long megaByte = 1024 * kiloByte;
        const long gigaByte = 1024 * megaByte;
        return sizeBytes switch
        {
            < kiloByte => $"{sizeBytes} B",
            < megaByte => $"{sizeBytes / (double)kiloByte:0.#} KB",
            < gigaByte => $"{sizeBytes / (double)megaByte:0.#} MB",
            _ => $"{sizeBytes / (double)gigaByte:0.#} GB",
        };
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float maximumWidth = float.IsFinite(availableSize.Width)
            ? MathF.Min(MaximumWidth, MathF.Max(0.0f, availableSize.Width))
            : MaximumWidth;
        float contentWidth = MathF.Max(0.0f, maximumWidth - (2.0f * HorizontalPadding));
        _detailsWidth = MathF.Min(MeasureText(_details), contentWidth);

        float remainingWidth = MathF.Max(0.0f, contentWidth - _detailsWidth);
        float gap = _detailsWidth > 0.0f && remainingWidth > TextGap ? TextGap : 0.0f;
        _fileNameWidth = MathF.Min(
            MeasureText(_fileName),
            MathF.Max(0.0f, remainingWidth - gap));
        _detailsX = HorizontalPadding + _fileNameWidth + gap;

        float desiredWidth = (2.0f * HorizontalPadding) + _fileNameWidth + gap + _detailsWidth;
        return new SizeF(MathF.Min(maximumWidth, desiredWidth), HeightDips);
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        float width = Bounds.Width;
        float height = Bounds.Height;
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

        context.DrawText(
            _fileName,
            TextFont,
            new Rect(HorizontalPadding, 0.0f, _fileNameWidth, height),
            Status.Message is not null
                ? (Status.IsError ? context.Palette.ErrorText : context.Palette.SecondaryText)
                : context.Palette.PrimaryText,
            DrawTextOptions.Clip);
        if (_detailsWidth > 0.0f)
        {
            context.DrawText(
                _details,
                TextFont,
                new Rect(_detailsX, 0.0f, _detailsWidth, height),
                context.Palette.SecondaryText,
                DrawTextOptions.Clip);
        }
    }

    private static (string FileName, string Details) GetText(ViewerStatus status)
    {
        if (status.Message is string message)
        {
            return (message, string.Empty);
        }

        string size = status.FileSizeBytes is { } sizeBytes
            ? $"   {FormatFileSize(sizeBytes)}"
            : string.Empty;
        return (
            status.FileName,
            $"{status.ImageWidth} × {status.ImageHeight}{size}   {status.ZoomPercentage:0}%");
    }

    private float MeasureText(string text)
    {
        if (text.Length == 0)
        {
            return 0.0f;
        }

        return TextLayouts
            .Get(text, TextFont, new SizeF(MaximumWidth, HeightDips))
            .Metrics.WidthIncludingTrailingWhitespace;
    }
}

internal readonly record struct ViewerStatus(
    string FileName,
    int ImageWidth,
    int ImageHeight,
    long? FileSizeBytes,
    float ZoomPercentage,
    string? Message,
    bool IsError);
