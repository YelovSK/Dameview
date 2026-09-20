using System.Diagnostics;
using System.Drawing;
using Dameview.Diagnostics;
using Dameview.UI.Foundation;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal sealed class PerformanceOverlay : UiElement, IDisposable
{
    private const float Padding = 10.0f;
    private const float ColumnWidth = 72.0f;
    private const float MaximumTextWidth = 1024.0f;
    private const float MaximumTextHeight = 256.0f;
    private static readonly long TextRefreshTicks = (long)(Stopwatch.Frequency * 0.25);

    /// <summary>How often an otherwise idle window repaints while the overlay is visible.</summary>
    internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(500.0);

    private readonly IDWriteFactory _directWriteFactory;
    private readonly IDWriteTextFormat _textFormat;
    private readonly PerformanceMonitor _monitor;
    private IDWriteTextLayout? _textLayout;
    private PerformanceSnapshot _lastActiveSnapshot;
    private SizeF _textSize;
    private long _lastTextRefresh;

    internal PerformanceOverlay(IDWriteFactory directWriteFactory, PerformanceMonitor monitor)
    {
        _directWriteFactory = directWriteFactory;
        _monitor = monitor;
        _textFormat = directWriteFactory.CreateTextFormat(
            UiTypography.FontFamily,
            FontWeight.Medium,
            FontStyle.Normal,
            13.0f);
        _textFormat.ParagraphAlignment = ParagraphAlignment.Near;
        _textFormat.TextAlignment = TextAlignment.Leading;
        _textFormat.IncrementalTabStop = ColumnWidth;
    }

    internal override bool IsHitTestVisible => false;

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        if (_textLayout is null)
        {
            RefreshTextLayout();
        }

        return new SizeF(
            MathF.Min(_textSize.Width + 2.0f * Padding, MathF.Max(0.0f, availableSize.Width)),
            MathF.Min(_textSize.Height + 2.0f * Padding, MathF.Max(0.0f, availableSize.Height)));
    }

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        long now = Stopwatch.GetTimestamp();
        if (_textLayout is null || now - _lastTextRefresh >= TextRefreshTicks)
        {
            SizeF previousTextSize = _textSize;
            RefreshTextLayout();
            _lastTextRefresh = now;

            // Only when the panel actually grew: InvalidateLayout also requests a
            // repaint, and an extra frame would be recorded as a sub-millisecond one.
            if (_textSize != previousTextSize)
            {
                InvalidateLayout();
            }
        }

        return false;
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        if (Bounds.Width <= 0.0f || Bounds.Height <= 0.0f)
        {
            return;
        }

        var panel = new RoundedRectangle(
            new RectangleF(PointF.Empty, Bounds.Size),
            UiDesign.PanelCornerRadius,
            UiDesign.PanelCornerRadius);
        context.FillRoundedRectangle(panel, context.Palette.OverlaySurface, 0.94f);
        context.DrawRoundedRectangle(panel, context.Palette.SurfaceBorder);
        if (_textLayout is not null)
        {
            context.DrawTextLayout(
                _textLayout,
                new System.Numerics.Vector2(Padding, Padding),
                context.Palette.PrimaryText,
                DrawTextOptions.Clip);
        }
    }

    public void Dispose()
    {
        _textLayout?.Dispose();
        _textFormat.Dispose();
    }

    private void RefreshTextLayout()
    {
        // The window empties whenever the app stops rendering, so keep showing the last
        // measured burst and mark it stale instead of dropping what the user stopped to read.
        PerformanceSnapshot snapshot = _monitor.Snapshot;
        bool idle = snapshot.SampleCount == 0;
        if (idle)
        {
            snapshot = _lastActiveSnapshot;
        }
        else
        {
            _lastActiveSnapshot = snapshot;
        }

        string text = snapshot.SampleCount == 0
            ? "Idle"
            : $"FPS\t{snapshot.FramesPerSecond:0.0}{(idle ? "\tidle" : string.Empty)}\n"
            + $"Frame\t{snapshot.AverageFrameMilliseconds:0.00} ms\tmax {snapshot.MaximumFrameMilliseconds:0.00}\n"
            + $"CPU\t{snapshot.AverageCpuMilliseconds:0.00} ms\tmax {snapshot.MaximumCpuMilliseconds:0.00}\n"
            + $"  update\t{snapshot.AverageUpdateMilliseconds:0.00} ms\n"
            + $"  layout\t{snapshot.AverageLayoutMilliseconds:0.00} ms\t{snapshot.LayoutPassesPerSecond:0}/s\n"
            + $"  draw\t{snapshot.AverageDrawMilliseconds:0.00} ms\t{snapshot.AverageDrawnElements:0} el \u00b7 {snapshot.AverageDrawOperations:0} ops\n"
            + $"  submit\t{snapshot.AverageSubmitMilliseconds:0.00} ms\n"
            + $"  other\t{snapshot.AverageOtherMilliseconds:0.00} ms\n"
            + $"Alloc\t{snapshot.AllocatedBytesPerSecond / (1024.0 * 1024.0):0.0} MB/s\n"
            + FormatGpuTime(snapshot);

        _textLayout?.Dispose();
        _textLayout = _directWriteFactory.CreateTextLayout(
            text,
            _textFormat,
            MaximumTextWidth,
            MaximumTextHeight);
        _textSize = new SizeF(
            MathF.Max(_textSize.Width, MathF.Ceiling(_textLayout.Metrics.WidthIncludingTrailingWhitespace)),
            MathF.Max(_textSize.Height, MathF.Ceiling(_textLayout.Metrics.Height)));
    }

    private static string FormatGpuTime(PerformanceSnapshot snapshot) =>
        snapshot.AverageGpuMilliseconds is { } average && snapshot.MaximumGpuMilliseconds is { } maximum
            ? $"GPU\t{average:0.00} ms\tmax {maximum:0.00}"
            : "GPU\t-- ms";
}
