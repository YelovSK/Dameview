using System.Drawing;
using Dameview.Imaging;
using Dameview.UI.Animation;
using Dameview.UI.Components;
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
    private const float ExpandedMinimumWidth = 380.0f;
    private const float RowHeight = 22.0f;
    private const float ExpandedTopPadding = 10.0f;
    private const float ExpandedBottomGap = 6.0f;
    private const float CornerRadius = 10.0f;
    private const double ExpandResponse = 18.0;
    private const float ButtonSize = 24.0f;
    // Centers the button vertically in the summary line, and keeps the same gap to the right edge.
    private const float ButtonInset = (HeightDips - ButtonSize) / 2.0f;
    private const float ButtonSpacing = 6.0f;
    private const float TrailingWidth = ButtonSpacing + ButtonSize + ButtonInset;
    private static readonly UiFont TextFont = new(13.0f, FontWeight.Medium);
    private static readonly UiFont RowFont = new(13.0f);

    private readonly AnimatedFloat _expansion;
    private readonly Button _expandButton;

    private string _fileName = string.Empty;
    private string _details = string.Empty;
    private float _fileNameWidth;
    private float _detailsWidth;
    private float _detailsX;
    private float _compactWidth;
    private float _labelWidth;
    private (string Label, string Value)[] _infoRows = [];

    internal StatusPanel(Action toggleExpanded, string toggleLabel)
    {
        Transition = new UiTransition(Fade: true, HiddenOffset: new PointF(0.0f, 8.0f), Response: 14.0);
        IsPresent = false;
        _expansion = Animate(0.0f, ExpandResponse);
        _expandButton = new Button(
            UiTypography.ChevronUpIcon,
            toggleExpanded,
            fontFamily: UiTypography.IconFontFamily,
            fontSize: 10.0f,
            filled: false)
        {
            ToolTip = new(toggleLabel),
            MaxWidth = ButtonSize,
            MaxHeight = ButtonSize,
        };
        AddChild(_expandButton);
    }

    private float ExpandedHeightDips =>
        HeightDips + ExpandedTopPadding + (_infoRows.Length * RowHeight) + ExpandedBottomGap;

    internal void SetInfo(ImageDetails info)
    {
        _infoRows = GetInfoRows(info);
        if (UsesExpandedBounds)
        {
            InvalidateLayout();
        }
    }

    internal bool IsExpanded
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            _expansion.SetTarget(value ? 1.0f : 0.0f);
            _expandButton.Label = value ? UiTypography.ChevronDownIcon : UiTypography.ChevronUpIcon;
            InvalidateLayout();
        }
    }

    // While expanded or shrinking back, the panel keeps its expanded bounds and draws its animated size inside them.
    private bool UsesExpandedBounds => IsExpanded || _expansion.Current > 0.0f;

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
        // Showing the panel lays it out anyway, so a hidden one need not relayout the tree.
        if (IsVisible)
        {
            InvalidateLayout();
        }
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
        _expandButton.Measure(new SizeF(ButtonSize, ButtonSize));
        float contentWidth = MathF.Max(0.0f, maximumWidth - HorizontalPadding - TrailingWidth);
        _detailsWidth = MathF.Min(MeasureText(_details), contentWidth);

        float remainingWidth = MathF.Max(0.0f, contentWidth - _detailsWidth);
        float gap = _detailsWidth > 0.0f && remainingWidth > TextGap ? TextGap : 0.0f;
        _fileNameWidth = MathF.Min(
            MeasureText(_fileName),
            MathF.Max(0.0f, remainingWidth - gap));
        _detailsX = HorizontalPadding + _fileNameWidth + gap;

        float desiredWidth = HorizontalPadding + _fileNameWidth + gap + _detailsWidth + TrailingWidth;
        _compactWidth = MathF.Min(maximumWidth, desiredWidth);
        if (!UsesExpandedBounds)
        {
            return new SizeF(_compactWidth, HeightDips);
        }

        _labelWidth = 0.0f;
        float valueWidth = 0.0f;
        foreach ((string label, string value) in _infoRows)
        {
            _labelWidth = MathF.Max(_labelWidth, MeasureText(label, RowFont));
            valueWidth = MathF.Max(valueWidth, MeasureText(value, RowFont));
        }

        float rowsWidth = (2.0f * HorizontalPadding) + _labelWidth + TextGap + valueWidth;
        float expandedWidth = MathF.Min(maximumWidth, MathF.Max(_compactWidth, MathF.Max(ExpandedMinimumWidth, rowsWidth)));
        return new SizeF(expandedWidth, ExpandedHeightDips);
    }

    // The button stays at the end of the summary line, which does not move as the panel expands.
    protected override void ArrangeCore(SizeF finalSize)
    {
        float summaryRight = ((finalSize.Width - _compactWidth) / 2.0f) + _compactWidth;
        _expandButton.Arrange(new RectangleF(
            summaryRight - ButtonInset - ButtonSize,
            finalSize.Height - HeightDips + ButtonInset,
            ButtonSize,
            ButtonSize));
    }

    // Only the button takes the pointer; the rest of the panel leaves the image underneath usable.
    protected override bool HitTestCore(PointF position) => false;

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        // Gives back the expanded bounds once the panel has shrunk to its summary line.
        if (!UsesExpandedBounds && Bounds.Height > HeightDips)
        {
            InvalidateLayout();
        }

        return false;
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        if (Bounds.Width <= 0.0f || Bounds.Height <= 0.0f)
        {
            return;
        }

        float expansion = Bounds.Height > HeightDips ? _expansion.Current : 0.0f;
        float width = Lerp(_compactWidth, Bounds.Width, expansion);
        float height = Lerp(HeightDips, Bounds.Height, expansion);
        var panelBounds = new RectangleF((Bounds.Width - width) / 2.0f, Bounds.Height - height, width, height);
        var panel = new RoundedRectangle(panelBounds, CornerRadius, CornerRadius);
        context.FillRoundedRectangle(panel, context.Palette.OverlaySurface);
        context.DrawRoundedRectangle(panel, context.Palette.SurfaceBorder);

        if (expansion > 0.0f)
        {
            DrawInfoRows(context, panelBounds, expansion);
        }

        // The summary line stays put at the bottom while the panel grows around it.
        float summaryX = (Bounds.Width - _compactWidth) / 2.0f;
        float summaryY = Bounds.Height - HeightDips;
        context.DrawText(
            _fileName,
            TextFont,
            new Rect(summaryX + HorizontalPadding, summaryY, _fileNameWidth, HeightDips),
            Status.Message is not null
                ? (Status.IsError ? context.Palette.ErrorText : context.Palette.SecondaryText)
                : context.Palette.PrimaryText,
            DrawTextOptions.Clip);
        if (_detailsWidth > 0.0f)
        {
            context.DrawText(
                _details,
                TextFont,
                new Rect(summaryX + _detailsX, summaryY, _detailsWidth, HeightDips),
                context.Palette.SecondaryText,
                DrawTextOptions.Clip);
        }
    }

    private void DrawInfoRows(in UiDrawContext context, RectangleF panelBounds, float expansion)
    {
        Color4 labelColor = WithOpacity(context.Palette.SecondaryText, expansion);
        Color4 valueColor = WithOpacity(context.Palette.PrimaryText, expansion);
        float left = panelBounds.X + HorizontalPadding;
        float valueX = left + _labelWidth + TextGap;
        float valueWidth = MathF.Max(0.0f, panelBounds.Right - HorizontalPadding - valueX);
        float rowsBottom = Bounds.Height - HeightDips - ExpandedBottomGap;

        // Rows sit on the summary line, so the growing panel uncovers them from the top.
        float clipTop = panelBounds.Y + (CornerRadius / 2.0f);
        context.PushClip(new RectangleF(
            panelBounds.X,
            clipTop,
            panelBounds.Width,
            MathF.Max(0.0f, rowsBottom - clipTop)));
        for (int index = 0; index < _infoRows.Length; index++)
        {
            float y = rowsBottom - ((_infoRows.Length - index) * RowHeight);
            (string label, string value) = _infoRows[index];
            context.DrawText(label, RowFont, new Rect(left, y, _labelWidth, RowHeight), labelColor, DrawTextOptions.Clip);
            context.DrawText(value, RowFont, new Rect(valueX, y, valueWidth, RowHeight), valueColor, DrawTextOptions.Clip);
        }

        context.PopClip();
    }

    // Leaves out what the file does not have, such as camera details on a screenshot.
    private static (string Label, string Value)[] GetInfoRows(ImageDetails info)
    {
        var rows = new List<(string Label, string Value)>();
        void Add(string label, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                rows.Add((label, value));
            }
        }

        // Camera models usually repeat the maker's name already.
        string? camera = info.CameraMaker is { } maker
            && info.CameraModel?.StartsWith(maker, StringComparison.OrdinalIgnoreCase) != true
            ? $"{maker} {info.CameraModel}".TrimEnd()
            : info.CameraModel;

        Add("Folder", info.Folder);
        Add("Type", JoinParts(info.Type, info.BitDepth is { } bitDepth ? $"{bitDepth}-bit" : null));
        Add("Modified", info.Modified);
        Add("Created", info.Created);
        Add("Taken", info.Taken);
        Add("Camera", camera);
        Add("Lens", info.Lens);
        Add("Exposure", JoinParts(info.FNumber, info.ExposureTime, info.Iso));
        Add("Focal length", info.FocalLength);
        return [.. rows];
    }

    private static string JoinParts(params string?[] parts) =>
        string.Join(" · ", parts.Where(part => !string.IsNullOrEmpty(part)));

    private static float Lerp(float from, float to, float amount) => from + ((to - from) * amount);

    private static Color4 WithOpacity(Color4 color, float opacity) => new(color.R, color.G, color.B, color.A * opacity);

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
            $"{status.ImageWidth} × {status.ImageHeight}{size}   {status.ZoomPercentage}%");
    }

    private float MeasureText(string text) => MeasureText(text, TextFont);

    private float MeasureText(string text, UiFont font)
    {
        if (text.Length == 0)
        {
            return 0.0f;
        }

        return TextLayouts
            .Get(text, font, new SizeF(MaximumWidth, HeightDips))
            .Metrics.WidthIncludingTrailingWhitespace;
    }
}

internal readonly record struct ViewerStatus(
    string FileName,
    int ImageWidth,
    int ImageHeight,
    long? FileSizeBytes,
    int ZoomPercentage,
    string? Message,
    bool IsError);
