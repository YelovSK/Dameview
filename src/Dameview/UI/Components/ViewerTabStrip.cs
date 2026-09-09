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
    private const float AddButtonWidthDips = 36.0f;
    private const float LabelPaddingDips = 12.0f;
    private const float WheelStepDips = 120.0f;

    private readonly IDWriteTextFormat _labelFormat;
    private readonly IDWriteTextFormat _closeFormat;
    private readonly IDWriteInlineObject _ellipsisSign;
    private readonly Button _addButton;
    private readonly Action<int> _selectionChanged;
    private readonly Action<int> _closeRequested;
    private readonly Action<ViewerTabInfo?, RectangleF>? _hoveredTabChanged;
    private readonly ScrollOffsetController _scrollOffset = new();
    private ViewerTabInfo[] _tabs = [];
    private int _selectedIndex;
    private int _hoveredIndex = -1;
    private bool _hoveringClose;
    private float _viewportWidth = -1.0f;

    internal ViewerTabStrip(
        IDWriteFactory factory,
        IReadOnlyList<ViewerTabInfo> tabs,
        int selectedIndex,
        Action<int> selectionChanged,
        Action<int> closeRequested,
        Action addRequested,
        Action<ViewerTabInfo?, RectangleF>? hoveredTabChanged = null)
    {
        _selectionChanged = selectionChanged;
        _closeRequested = closeRequested;
        _hoveredTabChanged = hoveredTabChanged;
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
        _addButton = new Button(
            factory,
            "+",
            addRequested,
            fontSize: 18.0f,
            backgroundInsetY: UiDesign.SmallSpacing);
        AddChild(_addButton);
        SetTabs(tabs, selectedIndex);
    }

    internal float ScrollOffset => _scrollOffset.Offset;
    internal override UiCursor Cursor => _hoveredIndex >= 0 ? UiCursor.Pointer : UiCursor.Default;

    internal void SetTabs(IReadOnlyList<ViewerTabInfo> tabs, int selectedIndex)
    {
        if (tabs.Count == 0)
        {
            throw new ArgumentException("A viewer tab strip requires at least one tab.", nameof(tabs));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)selectedIndex, (uint)tabs.Count);
        bool tabsChanged = !_tabs.SequenceEqual(tabs);
        if (!tabsChanged && _selectedIndex == selectedIndex)
        {
            return;
        }

        _tabs = [.. tabs];
        _selectedIndex = selectedIndex;
        if (tabsChanged)
        {
            SetHoveredTab(-1);
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
                SetHoveredTab(index);
                _hoveringClose = close;
                return new UiPointerResult(Consumed: true, NeedsRepaint: changed);

            case UiPointerEventKind.Pressed when input.Button == PointerButton.Primary:
                _hoveredTabChanged?.Invoke(null, RectangleF.Empty);
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

            case UiPointerEventKind.Pressed when input.Button == PointerButton.Middle:
                _hoveredTabChanged?.Invoke(null, RectangleF.Empty);
                if (index >= 0)
                {
                    _closeRequested(index);
                }

                return new UiPointerResult(Consumed: true, NeedsRepaint: index >= 0);

            case UiPointerEventKind.Wheel:
                SetHoveredTab(-1);
                _hoveringClose = false;
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
        float viewportWidth = TabViewportWidth;
        bool widthChanged = _viewportWidth != viewportWidth;
        _viewportWidth = viewportWidth;
        _addButton.Arrange(GetAddButtonBounds());
        UpdateScrollMetrics();
        if (widthChanged)
        {
            RevealSelectedTab();
        }
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        context.RenderTarget.PushAxisAlignedClip(
            new Rect(0.0f, 0.0f, TabViewportWidth, Bounds.Height),
            AntialiasMode.Aliased);
        float x = -_scrollOffset.Offset;
        try
        {
            for (int index = 0; index < _tabs.Length; index++)
            {
                DrawTab(context, index, x);
                x += TabWidthDips + UiDesign.SmallSpacing;
            }
        }
        finally
        {
            context.RenderTarget.PopAxisAlignedClip();
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
            SetHoveredTab(-1);
            _hoveringClose = false;
        }
    }

    public void Dispose()
    {
        _addButton.Dispose();
        _ellipsisSign.Dispose();
        _closeFormat.Dispose();
        _labelFormat.Dispose();
    }

    private float ContentWidth => _tabs.Length * TabWidthDips
        + Math.Max(0, _tabs.Length - 1) * UiDesign.SmallSpacing;

    private float TabViewportWidth
    {
        get
        {
            float addButtonWidth = MathF.Min(AddButtonWidthDips, Bounds.Width);
            float remaining = MathF.Max(0.0f, Bounds.Width - addButtonWidth);
            return MathF.Max(0.0f, remaining - MathF.Min(UiDesign.SmallSpacing, remaining));
        }
    }

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
            _tabs[index].Label,
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
        if (position.X < 0.0f || position.X >= TabViewportWidth)
        {
            return (-1, false);
        }

        float contentX = position.X + _scrollOffset.Offset;
        int index = (int)(contentX / (TabWidthDips + UiDesign.SmallSpacing));
        float xWithinTab = contentX - index * (TabWidthDips + UiDesign.SmallSpacing);
        if (index < 0 || index >= _tabs.Length || xWithinTab >= TabWidthDips)
        {
            return (-1, false);
        }

        return (index, xWithinTab >= TabWidthDips - CloseWidthDips);
    }

    private RectangleF GetTabBounds(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_tabs.Length);
        return new RectangleF(
            index * (TabWidthDips + UiDesign.SmallSpacing) - _scrollOffset.Offset,
            0.0f,
            TabWidthDips,
            Bounds.Height);
    }

    private void SetHoveredTab(int index)
    {
        if (_hoveredIndex == index)
        {
            return;
        }

        _hoveredIndex = index;
        _hoveredTabChanged?.Invoke(
            index >= 0 ? _tabs[index] : null,
            index >= 0 ? GetTabBounds(index) : RectangleF.Empty);
    }

    private void UpdateScrollMetrics()
    {
        _scrollOffset.SetMaximum(MathF.Max(0.0f, ContentWidth - TabViewportWidth));
    }

    private void RevealSelectedTab()
    {
        float viewportWidth = TabViewportWidth;
        if (viewportWidth <= 0.0f)
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
        else if (right > target + viewportWidth)
        {
            target = right - viewportWidth;
        }

        _scrollOffset.SetImmediate(target);
    }

    private RectangleF GetAddButtonBounds()
    {
        float width = MathF.Min(AddButtonWidthDips, Bounds.Width);
        return new RectangleF(
            MathF.Max(0.0f, Bounds.Width - width),
            0.0f,
            width,
            Bounds.Height);
    }
}
