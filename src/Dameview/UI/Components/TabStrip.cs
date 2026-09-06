using System.Drawing;
using Dameview.Platform;
using Dameview.UI.Layout;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal sealed class TabStrip : UiElement, IDisposable
{
    private readonly TabItem[] _items;
    private readonly StackPanel _row;
    private readonly Action<int> _selectionChanged;
    private int _selectedIndex;

    internal TabStrip(
        IDWriteFactory factory,
        IReadOnlyList<string> labels,
        int selectedIndex,
        Action<int> selectionChanged)
    {
        if (labels.Count == 0)
        {
            throw new ArgumentException("A tab strip requires at least one tab.", nameof(labels));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)selectedIndex, (uint)labels.Count);
        _selectionChanged = selectionChanged;
        _selectedIndex = selectedIndex;
        var textFormat = factory.CreateTextFormat(
            UiTypography.FontFamily, FontWeight.SemiBold, FontStyle.Normal, UiDesign.BodyFontSize);
        textFormat.TextAlignment = TextAlignment.Center;
        textFormat.ParagraphAlignment = ParagraphAlignment.Center;
        textFormat.WordWrapping = WordWrapping.NoWrap;
        TextFormat = textFormat;
        _items = new TabItem[labels.Count];
        for (int index = 0; index < labels.Count; index++)
        {
            _items[index] = new TabItem(this, index, labels[index], textFormat);
            _items[index].SetVisualState(UiVisualState.Selected, index == selectedIndex);
        }

        _row = new StackPanel(
            UiOrientation.Horizontal,
            UiDesign.SmallSpacing,
            StackPanelDistribution.Equal,
            _items);
        AddChild(_row);
    }

    internal int SelectedIndex
    {
        get => _selectedIndex;
        set => Select(value, notify: false, moveFocus: false);
    }

    internal UiElement SelectedTab => _items[_selectedIndex];

    private IDWriteTextFormat TextFormat { get; }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float width = float.IsFinite(availableSize.Width)
            ? availableSize.Width
            : _items.Length * 96.0f;
        var size = new SizeF(MathF.Max(0.0f, width), 36.0f);
        _row.Measure(size);
        return size;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        _row.Arrange(new RectangleF(PointF.Empty, finalSize));
    }

    protected override bool HitTestCore(PointF position) => false;

    public void Dispose() => TextFormat.Dispose();

    private void Select(int index, bool notify, bool moveFocus)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_items.Length);
        if (_selectedIndex != index)
        {
            _items[_selectedIndex].SetVisualState(UiVisualState.Selected, false);
            _selectedIndex = index;
            _items[_selectedIndex].SetVisualState(UiVisualState.Selected, true);
            if (notify)
            {
                _selectionChanged(index);
            }
        }

        if (moveFocus)
        {
            Root?.SetFocus(_items[index]);
        }
    }

    private sealed class TabItem : InteractiveControl
    {
        private readonly int _index;
        private readonly string _label;
        private readonly TabStrip _owner;
        private readonly IDWriteTextFormat _textFormat;

        internal TabItem(
            TabStrip owner,
            int index,
            string label,
            IDWriteTextFormat textFormat)
        {
            _owner = owner;
            _index = index;
            _label = label;
            _textFormat = textFormat;
        }

        internal override bool OnKeyEvent(UiKeyEvent input)
        {
            if (input.Key is UiKey.Left or UiKey.Right)
            {
                int direction = input.Key == UiKey.Left ? -1 : 1;
                int next = (_index + direction + _owner._items.Length) % _owner._items.Length;
                _owner.Select(next, notify: true, moveFocus: true);
                return true;
            }

            return base.OnKeyEvent(input);
        }

        protected override SizeF MeasureCore(SizeF availableSize) => new(96.0f, 36.0f);

        protected override void DrawCore(in UiDrawContext context)
        {
            float width = Bounds.Width;
            float height = Bounds.Height;
            var bounds = new RoundedRectangle(
                new RectangleF(0.0f, 0.0f, width, height),
                UiDesign.ControlCornerRadius,
                UiDesign.ControlCornerRadius);
            if (HasVisualState(UiVisualState.Selected))
            {
                context.FillRoundedRectangle(bounds, context.Palette.Accent, 0.18f);
            }

            if (HoverAmount > 0.0f)
            {
                context.FillRoundedRectangle(bounds, context.Palette.ControlHover, HoverAmount);
            }

            if (PressedAmount > 0.0f)
            {
                context.FillRoundedRectangle(bounds, context.Palette.ControlPressed, PressedAmount);
            }

            context.DrawText(
                _label,
                _textFormat,
                new Rect(0.0f, 0.0f, width, height),
                context.Palette.PrimaryText,
                DrawTextOptions.Clip);
        }

        protected override void Activate() => _owner.Select(_index, notify: true, moveFocus: false);
    }
}
