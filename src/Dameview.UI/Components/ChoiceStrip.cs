using System.Drawing;
using Dameview.UI.Animation;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

/// <summary>
/// A row of choices with exactly one selected. Like a radio group, only the selected segment
/// takes focus, and the arrow keys move the selection. The selection highlight slides from
/// one segment to the next. Subclasses draw the background and highlight, and may restyle the
/// segments too.
/// </summary>
internal abstract class ChoiceStrip<T> : UiElement
{
    private const float SegmentWidth = 96.0f;
    private const double SelectionResponse = 40.0;
    private static readonly UiFont LabelFont = new(UiDesign.BodyFontSize, FontWeight.SemiBold, TextAlignment.Center);

    private readonly Choice<T>[] _choices;
    private readonly Segment[] _segments;
    private readonly Action<T> _changed;
    private readonly float _segmentHeight;
    // Where the highlight is drawn, as a segment index that passes through the ones in between.
    private readonly AnimatedFloat _shownSelection;
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
        _shownSelection = Animate(_selectedIndex, SelectionResponse);
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

    protected override void DrawCore(in UiDrawContext context) =>
        DrawBackground(context, GetShownSelection());

    /// <summary>Draws behind the segments, including the selection highlight at its current place.</summary>
    protected abstract void DrawBackground(in UiDrawContext context, RectangleF selection);

    protected virtual void DrawSegment(
        in UiDrawContext context,
        RectangleF bounds,
        string label,
        bool selected,
        float hoverAmount)
    {
        // Hover only brightens the label, so it never stacks with the selection highlight.
        Color4 color = selected
            ? context.Palette.PrimaryText
            : Color4.Lerp(context.Palette.SecondaryText, context.Palette.PrimaryText, hoverAmount);
        context.DrawText(
            label,
            LabelFont,
            new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            color,
            DrawTextOptions.Clip);
    }

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
            // Only a choice the user just made slides there. One set from outside, like loaded
            // settings, is simply where things already are.
            if (notify)
            {
                _shownSelection.SetTarget(index);
                _changed(_choices[index].Value);
            }
            else
            {
                _shownSelection.SetValue(index);
            }
        }

        if (moveFocus)
        {
            Root?.SetFocus(_segments[index]);
        }
    }

    private RectangleF GetShownSelection()
    {
        float shown = _shownSelection.Current;
        int from = Math.Clamp((int)MathF.Floor(shown), 0, _segments.Length - 1);
        int to = Math.Min(from + 1, _segments.Length - 1);
        float progress = shown - from;
        RectangleF start = _segments[from].GetBoundsRelativeTo(this);
        RectangleF end = _segments[to].GetBoundsRelativeTo(this);
        return new RectangleF(
            start.X + ((end.X - start.X) * progress),
            start.Y,
            start.Width + ((end.Width - start.Width) * progress),
            start.Height);
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
            HoverAmount);

        // A click lands on an unselected segment, which can't hold focus until it is selected.
        protected override void Activate() => owner.Select(index, notify: true, moveFocus: true);
    }
}
