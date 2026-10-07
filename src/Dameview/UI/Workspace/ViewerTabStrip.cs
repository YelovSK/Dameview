using System.Drawing;
using Dameview.UI.Animation;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Workspace;

internal sealed class ViewerTabStrip : UiElement
{
    internal const float HeightDips = 32.0f;

    // Tabs share the strip down to the minimum width, then scroll. Below that a label
    // would be little more than an ellipsis.
    private const float MaximumTabWidthDips = 180.0f;
    private const float MinimumTabWidthDips = 72.0f;
    // Narrower tabs show their close button only when active or hovered, leaving the rest
    // of the room to the label.
    private const float AlwaysShowCloseWidthDips = 120.0f;
    private const float CloseWidthDips = 32.0f;
    private const float AddButtonWidthDips = 36.0f;
    private const float LabelPaddingDips = 12.0f;
    private const float WheelStepDips = 120.0f;
    private const double LayoutResponse = 20.0;
    private static readonly UiFont LabelFont = new(UiDesign.BodyFontSize, FontWeight.SemiBold, Ellipsis: true);
    private static readonly UiFont CloseFont = new(16.0f, Alignment: TextAlignment.Center);

    private readonly Button _addButton;
    private readonly Action<int> _selectionChanged;
    private readonly Action<int> _closeRequested;
    private readonly WorkspaceDragGesture _drag;
    private readonly Action<int, PointF>? _contextMenuRequested;
    private readonly ScrollOffsetController _scrollOffset = new();
    // Drawing eases towards the layout, which hit testing follows without delay.
    private readonly AnimatedFloat _drawnTabWidth;
    // In tabs rather than dips, so it stays on its tab while the width animates.
    private readonly AnimatedFloat _drawnSelection;
    // Whether this strip's pane is the one that takes input. The other panes' strips dim, which
    // marks the active pane without drawing anything over the image.
    private readonly AnimatedFloat _activeAmount;
    // How far each tab's label has brightened under the pointer. Hover only touches the label,
    // so it never stacks with the selection highlight.
    private AnimatedFloat[] _hoverAmounts = [];
    private ViewerTabInfo[] _tabs = [];
    private int _selectedIndex;
    private int _hoveredIndex = -1;
    private bool _hoveringClose;
    private int _pressedIndex = -1;
    private float _viewportWidth = -1.0f;

    internal ViewerTabStrip(
        IReadOnlyList<ViewerTabInfo> tabs,
        int selectedIndex,
        Action<int> selectionChanged,
        Action<int> closeRequested,
        Action addRequested,
        Action<int, WorkspaceDragEvent>? dragPointer = null,
        Action<int, PointF>? contextMenuRequested = null)
    {
        _selectionChanged = selectionChanged;
        _closeRequested = closeRequested;
        _drag = new WorkspaceDragGesture(drag => dragPointer?.Invoke(_pressedIndex, drag));
        _contextMenuRequested = contextMenuRequested;
        _addButton = new Button(
            "+",
            addRequested,
            fontSize: 18.0f,
            filled: false)
        {
            ToolTip = new("New tab"),
        };
        AddChild(_addButton);
        _drawnTabWidth = Animate(MaximumTabWidthDips, LayoutResponse);
        _drawnSelection = Animate(selectedIndex, LayoutResponse);
        _activeAmount = Animate(1.0f, UiDesign.HoverResponse);
        SetTabs(tabs, selectedIndex);
    }

    internal float ScrollOffset => _scrollOffset.Offset;
    /// <summary>The tab under the pointer and its bounds in the strip, which the tab preview follows.</summary>
    internal (ViewerTabInfo Tab, RectangleF Bounds)? HoveredTab =>
        _hoveredIndex >= 0 ? (_tabs[_hoveredIndex], GetTabBounds(_hoveredIndex)) : null;
    internal bool IsActive
    {
        set => _activeAmount.SetTarget(value ? 1.0f : 0.0f);
    }

    internal override float Opacity => base.Opacity * (0.5f + (0.5f * _activeAmount.Current));
    internal override WindowCursor Cursor => _hoveredIndex >= 0 ? WindowCursor.Pointer : WindowCursor.Default;

    internal override bool IsWindowDragArea(PointF position) => HitTestTab(position).Index < 0;

    internal int GetInsertionIndex(PointF position)
    {
        // A hidden strip keeps the bounds it last had, but takes no tabs.
        if (!IsVisible
            || position.X < 0.0f
            || position.X >= TabViewportWidth
            || position.Y < 0.0f
            || position.Y >= Bounds.Height)
        {
            return -1;
        }

        float contentX = position.X + _scrollOffset.Offset;
        return Math.Clamp((int)MathF.Floor((contentX + TabWidth / 2.0f) / TabPitch), 0, _tabs.Length);
    }

    internal RectangleF GetInsertionMarkerBounds(int insertionIndex)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)insertionIndex, (uint)_tabs.Length);
        const float markerWidth = 3.0f;
        float x = insertionIndex * TabPitch - _scrollOffset.Offset;
        return new RectangleF(x - markerWidth / 2.0f, 3.0f, markerWidth, Bounds.Height - 6.0f);
    }

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
            _hoverAmounts = [.. _tabs.Select(_ => new AnimatedFloat(0.0f, UiDesign.HoverResponse))];
        }

        _drawnTabWidth.SetTarget(TabWidth);
        _drawnSelection.SetTarget(selectedIndex);
        _addButton.Arrange(GetAddButtonBounds());
        UpdateScrollMetrics();
        RevealSelectedTab();
        InvalidateVisual();
    }

    internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
    {
        (int index, bool close) = HitTestTab(input.Position);
        switch (input.Kind)
        {
            case WindowPointerEventKind.Moved:
                if (_pressedIndex >= 0)
                {
                    bool dragging = _drag.Move(input.Position);
                    if (dragging)
                    {
                        SetHoveredTab(-1);
                        _hoveringClose = false;
                    }

                    return new UiPointerResult(Consumed: true, NeedsRepaint: dragging);
                }

                bool changed = index != _hoveredIndex || close != _hoveringClose;
                SetHoveredTab(index);
                _hoveringClose = close;
                return new UiPointerResult(Consumed: true, NeedsRepaint: changed);

            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Primary:
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

                    if (!close)
                    {
                        _pressedIndex = index;
                        _drag.Press(input.Position);
                    }
                }

                return new UiPointerResult(
                    Consumed: true,
                    NeedsRepaint: index >= 0,
                    CapturePointer: index >= 0 && !close);

            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Middle:
                if (index >= 0)
                {
                    _closeRequested(index);
                }

                return new UiPointerResult(Consumed: true, NeedsRepaint: index >= 0);

            case WindowPointerEventKind.Pressed when input.Button == PointerButton.Secondary:
                if (index >= 0)
                {
                    _contextMenuRequested?.Invoke(index, input.Position);
                }

                return new UiPointerResult(Consumed: true);

            case WindowPointerEventKind.Released:
                bool wasDragging = _drag.Release(input.Position);
                _pressedIndex = -1;
                return new UiPointerResult(Consumed: true, NeedsRepaint: wasDragging);

            case WindowPointerEventKind.Cancelled:
                bool cancelledDrag = _drag.Cancel(input.Position);
                _pressedIndex = -1;
                return new UiPointerResult(Consumed: true, NeedsRepaint: cancelledDrag);

            case WindowPointerEventKind.Wheel:
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
        if (widthChanged)
        {
            // Resizing the window follows the pointer, so only a change in tabs animates.
            _drawnTabWidth.SetValue(TabWidth);
            RevealSelectedTab();
        }

        _addButton.Arrange(GetAddButtonBounds());
        UpdateScrollMetrics();
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        context.PushClip(new RectangleF(0.0f, 0.0f, TabViewportWidth, Bounds.Height));
        float x = -_scrollOffset.Offset;
        float width = _drawnTabWidth.Current;
        try
        {
            context.FillRoundedRectangle(
                new RoundedRectangle(
                    new RectangleF(
                        x + (_drawnSelection.Current * (width + UiDesign.SmallSpacing)),
                        0.0f,
                        width,
                        Bounds.Height),
                    UiDesign.ControlCornerRadius,
                    UiDesign.ControlCornerRadius),
                context.Palette.Accent,
                0.06f + (0.12f * _activeAmount.Current));
            for (int index = 0; index < _tabs.Length; index++)
            {
                DrawTab(context, index, x, width);
                x += width + UiDesign.SmallSpacing;
            }
        }
        finally
        {
            context.PopClip();
        }
    }

    protected override bool UpdateCore(in UiUpdateContext context)
    {
        _addButton.Arrange(GetAddButtonBounds());
        bool hoverChanging = false;
        foreach (AnimatedFloat hoverAmount in _hoverAmounts)
        {
            hoverChanging |= hoverAmount.Update(context);
        }

        if (hoverChanging)
        {
            InvalidateVisual();
        }

        return _scrollOffset.Update(context) | hoverChanging;
    }

    protected override void OnVisualStateChanged()
    {
        if (!HasVisualState(UiVisualState.Hovered))
        {
            SetHoveredTab(-1);
            _hoveringClose = false;
        }
    }

    private float ContentWidth => _tabs.Length * TabWidth
        + Math.Max(0, _tabs.Length - 1) * UiDesign.SmallSpacing;

    private float TabWidth => _tabs.Length == 0
        ? MaximumTabWidthDips
        : Math.Clamp(
            (TabViewportWidth + UiDesign.SmallSpacing) / _tabs.Length - UiDesign.SmallSpacing,
            MinimumTabWidthDips,
            MaximumTabWidthDips);

    private float TabPitch => TabWidth + UiDesign.SmallSpacing;

    private bool ShowsClose(int index, float width) =>
        width >= AlwaysShowCloseWidthDips || index == _selectedIndex || index == _hoveredIndex;

    private float TabViewportWidth
    {
        get
        {
            float addButtonWidth = MathF.Min(AddButtonWidthDips, Bounds.Width);
            float remaining = MathF.Max(0.0f, Bounds.Width - addButtonWidth);
            return MathF.Max(0.0f, remaining - MathF.Min(UiDesign.SmallSpacing, remaining));
        }
    }

    private void DrawTab(in UiDrawContext context, int index, float x, float width)
    {
        bool showsClose = ShowsClose(index, width);
        float labelEnd = showsClose ? width - CloseWidthDips - UiDesign.SmallSpacing : width - LabelPaddingDips;
        context.DrawText(
            _tabs[index].Label,
            LabelFont,
            new Rect(
                x + LabelPaddingDips,
                0.0f,
                MathF.Max(0.0f, labelEnd - LabelPaddingDips),
                Bounds.Height),
            index == _selectedIndex
                ? context.Palette.PrimaryText
                : Color4.Lerp(context.Palette.SecondaryText, context.Palette.PrimaryText, _hoverAmounts[index].Current),
            DrawTextOptions.Clip);

        if (!showsClose)
        {
            return;
        }

        var closeBounds = new Rect(
            x + width - CloseWidthDips,
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
            CloseFont,
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

        float width = TabWidth;
        float contentX = position.X + _scrollOffset.Offset;
        int index = (int)(contentX / TabPitch);
        float xWithinTab = contentX - index * TabPitch;
        if (index < 0 || index >= _tabs.Length || xWithinTab >= width)
        {
            return (-1, false);
        }

        // Any tab under the pointer is hovered and so shows its close button.
        return (index, xWithinTab >= width - CloseWidthDips);
    }

    private RectangleF GetTabBounds(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_tabs.Length);
        return new RectangleF(
            index * TabPitch - _scrollOffset.Offset,
            0.0f,
            TabWidth,
            Bounds.Height);
    }

    private void SetHoveredTab(int index)
    {
        if (_hoveredIndex == index)
        {
            return;
        }

        // The tabs may have just been replaced, leaving the old index past the end.
        if (_hoveredIndex >= 0 && _hoveredIndex < _hoverAmounts.Length)
        {
            _hoverAmounts[_hoveredIndex].SetTarget(0.0f);
        }

        if (index >= 0)
        {
            _hoverAmounts[index].SetTarget(1.0f);
        }

        _hoveredIndex = index;
        Root?.RefreshToolTip(this);
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

        float left = _selectedIndex * TabPitch;
        float right = left + TabWidth;
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

    // Follows the last tab, and stays at the edge once the tabs overflow and scroll.
    private RectangleF GetAddButtonBounds()
    {
        float width = MathF.Min(AddButtonWidthDips, Bounds.Width);
        float drawnContentWidth = _tabs.Length * (_drawnTabWidth.Current + UiDesign.SmallSpacing)
            - UiDesign.SmallSpacing;
        float x = MathF.Min(drawnContentWidth, TabViewportWidth) + UiDesign.SmallSpacing;
        return new RectangleF(
            MathF.Min(x, MathF.Max(0.0f, Bounds.Width - width)),
            0.0f,
            width,
            Bounds.Height);
    }
}
