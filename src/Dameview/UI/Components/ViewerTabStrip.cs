using System.Drawing;
using Dameview.Platform;
using Dameview.UI.Layout;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal sealed class ViewerTabStrip : UiElement, IDisposable
{
    internal const float HeightDips = 36.0f;

    private const float TabWidthDips = 180.0f;
    private const float CloseWidthDips = 32.0f;
    private const float LabelPaddingDips = 12.0f;
    private const float WheelStepDips = 120.0f;

    private readonly IDWriteTextFormat _labelFormat;
    private readonly IDWriteTextFormat _closeFormat;
    private readonly IDWriteInlineObject _ellipsisSign;
    private readonly Action<int> _selectionChanged;
    private readonly Action<int> _closeRequested;
    private readonly ScrollOffsetController _scrollOffset = new();
    private string[] _labels = [];
    private int _selectedIndex;
    private int _hoveredIndex = -1;
    private bool _hoveringClose;
    private float _viewportWidth = -1.0f;

    internal ViewerTabStrip(
        IDWriteFactory factory,
        IReadOnlyList<string> labels,
        int selectedIndex,
        Action<int> selectionChanged,
        Action<int> closeRequested)
    {
        _selectionChanged = selectionChanged;
        _closeRequested = closeRequested;
        _labelFormat = factory.CreateTextFormat(
            UiTypography.FontFamily, FontWeight.SemiBold, FontStyle.Normal, UiDesign.BodyFontSize);
        _labelFormat.TextAlignment = TextAlignment.Leading;
        _labelFormat.ParagraphAlignment = ParagraphAlignment.Center;
        _labelFormat.WordWrapping = WordWrapping.NoWrap;
        _ellipsisSign = factory.CreateEllipsisTrimmingSign(_labelFormat);
        _labelFormat.SetTrimming(
            new Trimming { Granularity = TrimmingGranularity.Character },
            _ellipsisSign);
        _closeFormat = factory.CreateTextFormat(
            UiTypography.FontFamily, FontWeight.Normal, FontStyle.Normal, 16.0f);
        _closeFormat.TextAlignment = TextAlignment.Center;
        _closeFormat.ParagraphAlignment = ParagraphAlignment.Center;
        _closeFormat.WordWrapping = WordWrapping.NoWrap;
        SetTabs(labels, selectedIndex);
    }

    internal float ScrollOffset => _scrollOffset.Offset;
    internal override UiCursor Cursor => _hoveredIndex >= 0 ? UiCursor.Pointer : UiCursor.Default;

    internal void SetTabs(IReadOnlyList<string> labels, int selectedIndex)
    {
        if (labels.Count == 0)
        {
            throw new ArgumentException("A viewer tab strip requires at least one tab.", nameof(labels));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)selectedIndex, (uint)labels.Count);
        bool labelsChanged = !_labels.SequenceEqual(labels);
        if (!labelsChanged && _selectedIndex == selectedIndex)
        {
            return;
        }

        _labels = [.. labels];
        _selectedIndex = selectedIndex;
        if (labelsChanged)
        {
            _hoveredIndex = -1;
            _hoveringClose = false;
        }

        UpdateScrollMetrics();
        RevealSelectedTab();
        InvalidateVisual();
    }

    internal override UiPointerResult OnPointerEvent(in UiPointerEvent input)
    {
        (int index, bool close) = HitTestTab(input.Position);
        switch (input.Kind)
        {
            case UiPointerEventKind.Moved:
                bool changed = index != _hoveredIndex || close != _hoveringClose;
                _hoveredIndex = index;
                _hoveringClose = close;
                return new UiPointerResult(Consumed: true, NeedsRepaint: changed);

            case UiPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                if (index >= 0)
                {
                    if (close)
                    {
                        _closeRequested(index);
                    }
                    else if (index != _selectedIndex)
                    {
                        _selectionChanged(index);
                    }
                }

                return new UiPointerResult(Consumed: true, NeedsRepaint: index >= 0);

            case UiPointerEventKind.Wheel:
                bool scrollChanged = _scrollOffset.ScrollBy(
                    -input.WheelDelta / 120.0f * WheelStepDips);
                return new UiPointerResult(Consumed: true, NeedsRepaint: scrollChanged);

            default:
                return new UiPointerResult(Consumed: true);
        }
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float width = float.IsFinite(availableSize.Width)
            ? availableSize.Width
            : ContentWidth;
        return new SizeF(MathF.Max(0.0f, width), HeightDips);
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        bool widthChanged = _viewportWidth != finalSize.Width;
        _viewportWidth = finalSize.Width;
        UpdateScrollMetrics();
        if (widthChanged)
        {
            RevealSelectedTab();
        }
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        float x = -_scrollOffset.Offset;
        for (int index = 0; index < _labels.Length; index++)
        {
            DrawTab(context, index, x);
            x += TabWidthDips + UiDesign.SmallSpacing;
        }
    }

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        return _scrollOffset.Update(context.ElapsedSeconds);
    }

    protected override void OnVisualStateChanged()
    {
        if (!HasVisualState(UiVisualState.Hovered))
        {
            _hoveredIndex = -1;
            _hoveringClose = false;
        }
    }

    public void Dispose()
    {
        _ellipsisSign.Dispose();
        _closeFormat.Dispose();
        _labelFormat.Dispose();
    }

    private float ContentWidth => _labels.Length * TabWidthDips
        + Math.Max(0, _labels.Length - 1) * UiDesign.SmallSpacing;

    private void DrawTab(in UiDrawContext context, int index, float x)
    {
        var bounds = new RoundedRectangle(
            new RectangleF(x, 0.0f, TabWidthDips, Bounds.Height),
            UiDesign.ControlCornerRadius,
            UiDesign.ControlCornerRadius);
        if (index == _selectedIndex)
        {
            context.FillRoundedRectangle(bounds, context.Palette.Accent, 0.18f);
        }

        if (index == _hoveredIndex)
        {
            context.FillRoundedRectangle(bounds, context.Palette.ControlHover);
        }

        context.DrawText(
            _labels[index],
            _labelFormat,
            new Rect(
                x + LabelPaddingDips,
                0.0f,
                TabWidthDips - CloseWidthDips - UiDesign.SmallSpacing - LabelPaddingDips,
                Bounds.Height),
            context.Palette.PrimaryText,
            DrawTextOptions.Clip);

        var closeBounds = new Rect(
            x + TabWidthDips - CloseWidthDips,
            0.0f,
            CloseWidthDips,
            Bounds.Height);
        if (index == _hoveredIndex && _hoveringClose)
        {
            const float inset = 4.0f;
            context.FillRoundedRectangle(
                new RoundedRectangle(
                    new RectangleF(
                        closeBounds.Left + inset,
                        inset,
                        CloseWidthDips - 2.0f * inset,
                        Bounds.Height - 2.0f * inset),
                    UiDesign.ControlCornerRadius,
                    UiDesign.ControlCornerRadius),
                context.Palette.ControlPressed);
        }

        context.DrawText(
            "×",
            _closeFormat,
            closeBounds,
            context.Palette.PrimaryText,
            DrawTextOptions.Clip);
    }

    private (int Index, bool Close) HitTestTab(PointF position)
    {
        float contentX = position.X + _scrollOffset.Offset;
        int index = (int)(contentX / (TabWidthDips + UiDesign.SmallSpacing));
        float xWithinTab = contentX - index * (TabWidthDips + UiDesign.SmallSpacing);
        if (index < 0 || index >= _labels.Length || xWithinTab >= TabWidthDips)
        {
            return (-1, false);
        }

        return (index, xWithinTab >= TabWidthDips - CloseWidthDips);
    }

    private void UpdateScrollMetrics()
    {
        _scrollOffset.SetMaximum(MathF.Max(0.0f, ContentWidth - Bounds.Width));
    }

    private void RevealSelectedTab()
    {
        if (Bounds.Width <= 0.0f)
        {
            return;
        }

        float left = _selectedIndex * (TabWidthDips + UiDesign.SmallSpacing);
        float right = left + TabWidthDips;
        float target = _scrollOffset.TargetOffset;
        if (left < target)
        {
            target = left;
        }
        else if (right > target + Bounds.Width)
        {
            target = right - Bounds.Width;
        }

        _scrollOffset.SetImmediate(target);
    }
}
