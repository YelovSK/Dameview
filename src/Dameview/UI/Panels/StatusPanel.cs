using System.Drawing;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class StatusPanel : UiElement, IDisposable
{
    internal const float HeightDips = 32.0f;

    private const float HorizontalPadding = 12.0f;
    private const float TextGap = 12.0f;
    private const float MaximumWidth = 720.0f;

    private readonly IDWriteTextFormat _fileNameFormat;
    private readonly IDWriteTextFormat _detailsFormat;
    private readonly AnimatedFloat _visibility = new(0.0f, 14.0);
    private string _fileName = string.Empty;
    private string _details = string.Empty;
    private float _fileNameWidth;
    private float _detailsWidth;
    private float _detailsX;
    private bool _pointerNear;

    internal StatusPanel(
        IDWriteFactory directWriteFactory)
    {
        _fileNameFormat = CreateFormat(directWriteFactory, TextAlignment.Leading);
        _detailsFormat = CreateFormat(directWriteFactory, TextAlignment.Leading);
    }

    private ViewerStatus Status { get; set; }

    internal void SetStatus(ViewerStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        (_fileName, _details) = GetText(status);
        UpdateVisibility();
        InvalidateLayout();
    }

    internal override bool IsHitTestVisible => false;
    internal override float Opacity => _visibility.Current;
    internal override PointF VisualOffset => new(0.0f, (1.0f - _visibility.Current) * 8.0f);

    internal void SetPointerNear(bool pointerNear)
    {
        if (_pointerNear == pointerNear)
        {
            return;
        }

        _pointerNear = pointerNear;
        UpdateVisibility();
    }

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
        _detailsWidth = MathF.Min(MeasureText(_details, _detailsFormat), contentWidth);

        float remainingWidth = MathF.Max(0.0f, contentWidth - _detailsWidth);
        float gap = _detailsWidth > 0.0f && remainingWidth > TextGap ? TextGap : 0.0f;
        _fileNameWidth = MathF.Min(
            MeasureText(_fileName, _fileNameFormat),
            MathF.Max(0.0f, remainingWidth - gap));
        _detailsX = HorizontalPadding + _fileNameWidth + gap;

        float desiredWidth = (2.0f * HorizontalPadding) + _fileNameWidth + gap + _detailsWidth;
        return new SizeF(MathF.Min(maximumWidth, desiredWidth), HeightDips);
    }

    protected override bool UpdateCore(in UiUpdateContext context) => _visibility.Update(context);

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
            _fileNameFormat,
            new Rect(HorizontalPadding, 0.0f, _fileNameWidth, height),
            Status.Message is not null
                ? (Status.IsError ? context.Palette.ErrorText : context.Palette.SecondaryText)
                : context.Palette.PrimaryText,
            DrawTextOptions.Clip);
        if (_detailsWidth > 0.0f)
        {
            context.DrawText(
                _details,
                _detailsFormat,
                new Rect(_detailsX, 0.0f, _detailsWidth, height),
                context.Palette.SecondaryText,
                DrawTextOptions.Clip);
        }
    }

    public void Dispose()
    {
        _detailsFormat.Dispose();
        _fileNameFormat.Dispose();
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

    private float MeasureText(string text, IDWriteTextFormat format)
    {
        if (text.Length == 0)
        {
            return 0.0f;
        }

        // Layout is invalidated as a whole, so one pane's status changing re-measures every
        // other pane too. Text that did not change has to come back out of the cache.
        UiTextLayoutCache layouts = Root?.TextLayouts
            ?? throw new InvalidOperationException(
                "The status panel measures text only once it is attached to a root.");
        return layouts
            .Get(text, format, new SizeF(MaximumWidth, HeightDips))
            .Metrics.WidthIncludingTrailingWhitespace;
    }

    private void UpdateVisibility()
    {
        bool visible = _pointerNear || Status.Message is not null;
        if (_visibility.SetTarget(visible ? 1.0f : 0.0f))
        {
            InvalidateVisual();
        }
    }

    private static IDWriteTextFormat CreateFormat(
        IDWriteFactory factory,
        TextAlignment textAlignment)
    {
        IDWriteTextFormat format = factory.CreateTextFormat(
            UiTypography.FontFamily,
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
    long? FileSizeBytes,
    float ZoomPercentage,
    string? Message,
    bool IsError);
