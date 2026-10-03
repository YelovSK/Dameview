using System.Drawing;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.Win32.Input;

namespace Dameview.UI.Components;

/// <summary>
/// A row of choices with exactly one selected. Like a radio group, only the selected segment
/// takes focus, and the arrow keys move the selection. Subclasses decide how it looks.
/// </summary>
internal abstract class ChoiceStrip<T> : UiElement
{
    private const float SegmentWidth = 96.0f;

    private readonly Choice<T>[] _choices;
    private readonly Segment[] _segments;
    private readonly Action<T> _changed;
    private readonly float _segmentHeight;
    private int _selectedIndex;

    protected ChoiceStrip(
        IReadOnlyList<Choice<T>> choices,
        T selectedValue,
        Action<T> changed,
        float segmentHeight,
        float spacing,
        float padding)
    {
        if (choices.Count == 0)
        {
            throw new ArgumentException("A choice strip requires at least one choice.", nameof(choices));
        }

        _choices = [.. choices];
        _changed = changed;
        _segmentHeight = segmentHeight;
        _selectedIndex = FindIndex(selectedValue);
        _segments = new Segment[_choices.Length];
        for (int index = 0; index < _segments.Length; index++)
        {
            _segments[index] = new Segment(this, index);
            _segments[index].SetVisualState(UiVisualState.Selected, index == _selectedIndex);
        }

        AddChild(new StackPanel(UiOrientation.Horizontal, _segments)
        {
            Spacing = spacing,
            Distribution = StackPanelDistribution.Equal,
            Margin = new UiThickness(padding),
        });
    }

    internal T SelectedValue
    {
        get => _choices[_selectedIndex].Value;
        set => Select(FindIndex(value), notify: false, moveFocus: false);
    }

    internal UiElement SelectedSegment => _segments[_selectedIndex];

    internal void SetChoiceLabel(T value, string label)
    {
        int index = FindIndex(value);
        _choices[index] = _choices[index] with { Label = label };
        _segments[index].InvalidateLabel();
    }

    protected override bool HitTestCore(PointF position) => false;

    protected abstract void DrawSegment(
        in UiDrawContext context,
        RectangleF bounds,
        string label,
        bool selected,
        float hoverAmount,
        float pressedAmount);

    private int FindIndex(T value)
    {
        int index = Array.FindIndex(
            _choices,
            choice => EqualityComparer<T>.Default.Equals(choice.Value, value));
        return index >= 0
            ? index
            : throw new ArgumentOutOfRangeException(nameof(value), "The value is not one of the choices.");
    }

    private void Select(int index, bool notify, bool moveFocus)
    {
        if (_selectedIndex != index)
        {
            _segments[_selectedIndex].SetVisualState(UiVisualState.Selected, false);
            _selectedIndex = index;
            _segments[_selectedIndex].SetVisualState(UiVisualState.Selected, true);
            if (notify)
            {
                _changed(_choices[index].Value);
            }
        }

        if (moveFocus)
        {
            Root?.SetFocus(_segments[index]);
        }
    }

    private sealed class Segment(ChoiceStrip<T> owner, int index) : InteractiveControl
    {
        internal override bool IsFocusable => base.IsFocusable && index == owner._selectedIndex;

        internal void InvalidateLabel() => InvalidateVisual();

        internal override bool OnKeyEvent(WindowKeyEvent input)
        {
            if (input.Key is WindowKey.Left or WindowKey.Right)
            {
                int direction = input.Key == WindowKey.Left ? -1 : 1;
                int count = owner._segments.Length;
                owner.Select((index + direction + count) % count, notify: true, moveFocus: true);
                return true;
            }

            return base.OnKeyEvent(input);
        }

        protected override SizeF MeasureCore(SizeF availableSize) => new(SegmentWidth, owner._segmentHeight);

        protected override void DrawCore(in UiDrawContext context) => owner.DrawSegment(
            context,
            new RectangleF(PointF.Empty, Bounds.Size),
            owner._choices[index].Label,
            HasVisualState(UiVisualState.Selected),
            HoverAmount,
            PressedAmount);

        // A click lands on an unselected segment, which can't hold focus until it is selected.
        protected override void Activate() => owner.Select(index, notify: true, moveFocus: true);
    }
}
