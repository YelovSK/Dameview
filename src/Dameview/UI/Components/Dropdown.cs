using System.Drawing;
using Dameview.Platform;
using Dameview.UI.Layout;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Components;

internal readonly record struct DropdownOption<T>(string Label, T Value);

internal sealed class Dropdown<T> : InteractiveControl, IDisposable
{
    private readonly Action<T> _changed;
    private readonly UiDesignTokens _design;
    private readonly DropdownOption<T>[] _options;
    private readonly PopupHost _popupHost;
    private readonly PopupList _popupList;
    private readonly IDWriteTextFormat _textFormat;
    private int _selectedIndex;

    internal Dropdown(
        IDWriteFactory factory,
        PopupHost popupHost,
        IReadOnlyList<DropdownOption<T>> options,
        T selectedValue,
        Action<T> changed,
        UiDesignTokens? design = null)
        : base(design ?? UiDesignTokens.Default)
    {
        if (options.Count == 0)
        {
            throw new ArgumentException("A dropdown requires at least one option.", nameof(options));
        }

        _design = design ?? UiDesignTokens.Default;
        _popupHost = popupHost;
        _options = [.. options];
        _changed = changed;
        _selectedIndex = FindIndex(selectedValue);
        _textFormat = factory.CreateTextFormat(
            "Segoe UI Variable", FontWeight.SemiBold, FontStyle.Normal, _design.BodyFontSize);
        _textFormat.ParagraphAlignment = ParagraphAlignment.Center;
        _textFormat.WordWrapping = WordWrapping.NoWrap;
        _popupList = new PopupList(factory, _options, SelectFromPopup, _design);
        _popupList.SelectedIndex = _selectedIndex;
    }

    internal bool IsOpen => HasVisualState(UiVisualState.Open);
    internal int SelectedIndex => _selectedIndex;
    internal T SelectedValue
    {
        get => _options[_selectedIndex].Value;
        set => Select(FindIndex(value), notify: false);
    }

    internal void SetOptionLabel(T value, string label)
    {
        int index = FindIndex(value);
        if (_options[index].Label == label)
        {
            return;
        }

        _options[index] = _options[index] with { Label = label };
        _popupList.SetLabel(index, label);
        if (index == _selectedIndex)
        {
            InvalidateVisual();
        }
    }

    internal override bool OnKeyEvent(UiKeyEvent input)
    {
        if (!IsEnabled)
        {
            return false;
        }

        if (input.Key == UiKey.Escape && IsOpen)
        {
            ClosePopup();
            return true;
        }

        if (input.Key is UiKey.Up or UiKey.Down)
        {
            int direction = input.Key == UiKey.Up ? -1 : 1;
            int next = (_selectedIndex + direction + _options.Length) % _options.Length;
            Select(next, notify: true);
            return true;
        }

        return base.OnKeyEvent(input);
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float width = float.IsFinite(availableSize.Width) ? availableSize.Width : 180.0f;
        return new SizeF(MathF.Max(0.0f, width), 36.0f);
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        float width = Bounds.Width;
        float height = Bounds.Height;
        var bounds = new RoundedRectangle(
            new RectangleF(0.0f, 0.0f, width, height),
            _design.ControlCornerRadius,
            _design.ControlCornerRadius);
        context.DrawRoundedRectangle(bounds, context.Palette.SurfaceBorder);
        if (IsOpen)
        {
            context.FillRoundedRectangle(bounds, context.Palette.Accent, 0.14f);
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
            _options[_selectedIndex].Label,
            _textFormat,
            new Rect(12.0f, 0.0f, MathF.Max(12.0f, width - 36.0f), height),
            IsEnabled ? context.Palette.PrimaryText : context.Palette.SecondaryText,
            DrawTextOptions.Clip);
        context.DrawText(
            IsOpen ? "⌃" : "⌄",
            _textFormat,
            new Rect(MathF.Max(0.0f, width - 28.0f), 0.0f, width, height),
            context.Palette.SecondaryText,
            DrawTextOptions.Clip);

        if (HasVisualState(UiVisualState.Focused))
        {
            context.DrawRoundedRectangle(new RoundedRectangle(
                new RectangleF(2.0f, 2.0f, MathF.Max(0.0f, width - 4.0f), MathF.Max(0.0f, height - 4.0f)),
                _design.ControlCornerRadius - 2.0f,
                _design.ControlCornerRadius - 2.0f), context.Palette.Accent, 2.0f);
        }
    }

    protected override void Activate()
    {
        if (IsOpen)
        {
            ClosePopup();
            return;
        }

        SetVisualState(UiVisualState.Open, true);
        _popupHost.Show(this, _popupList, _popupList.PreferredSize(Bounds.Width), OnPopupClosed);
    }

    protected override void OnVisualStateChanged()
    {
        base.OnVisualStateChanged();
        if (!IsEnabled && IsOpen)
        {
            ClosePopup();
        }
    }

    public void Dispose()
    {
        if (IsOpen)
        {
            _popupHost.Close();
        }

        _popupList.Dispose();
        _textFormat.Dispose();
    }

    private int FindIndex(T value)
    {
        int index = Array.FindIndex(
            _options,
            option => EqualityComparer<T>.Default.Equals(option.Value, value));
        return index >= 0
            ? index
            : throw new ArgumentOutOfRangeException(nameof(value), "The value is not a dropdown option.");
    }

    private void Select(int index, bool notify)
    {
        if (_selectedIndex == index)
        {
            return;
        }

        _selectedIndex = index;
        _popupList.SelectedIndex = index;
        InvalidateVisual();
        if (notify)
        {
            _changed(_options[index].Value);
        }
    }

    private void SelectFromPopup(int index)
    {
        Select(index, notify: true);
        ClosePopup();
        Root?.SetFocus(this);
    }

    private void ClosePopup() => _popupHost.Close();

    private void OnPopupClosed()
    {
        SetVisualState(UiVisualState.Open, false);
    }

    private sealed class PopupList : UiElement, IDisposable
    {
        private const float Padding = 4.0f;
        private const float ItemHeight = 36.0f;

        private readonly Button[] _buttons;
        private readonly StackPanel _column;
        private readonly UiDesignTokens _design;

        internal PopupList(
            IDWriteFactory factory,
            IReadOnlyList<DropdownOption<T>> options,
            Action<int> selected,
            UiDesignTokens design)
        {
            _design = design;
            _buttons = new Button[options.Count];
            for (int index = 0; index < options.Count; index++)
            {
                int optionIndex = index;
                _buttons[index] = new Button(
                    factory,
                    options[index].Label,
                    () => selected(optionIndex),
                    design);
            }

            _column = new StackPanel(
                UiOrientation.Vertical,
                design.SmallSpacing,
                StackPanelDistribution.Equal,
                _buttons);
            AddChild(_column);
        }

        internal int SelectedIndex
        {
            set
            {
                for (int index = 0; index < _buttons.Length; index++)
                {
                    _buttons[index].IsSelected = index == value;
                }
            }
        }

        internal void SetLabel(int index, string label) => _buttons[index].Label = label;

        internal SizeF PreferredSize(float anchorWidth)
        {
            float height = 2.0f * Padding
                + _buttons.Length * ItemHeight
                + (_buttons.Length - 1) * _design.SmallSpacing;
            return new SizeF(MathF.Max(160.0f, anchorWidth), height);
        }

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            var contentSize = new SizeF(
                MathF.Max(0.0f, availableSize.Width - 2.0f * Padding),
                MathF.Max(0.0f, availableSize.Height - 2.0f * Padding));
            _column.Measure(contentSize);
            return availableSize;
        }

        protected override void ArrangeCore(SizeF finalSize)
        {
            _column.Arrange(new RectangleF(
                Padding,
                Padding,
                MathF.Max(0.0f, finalSize.Width - 2.0f * Padding),
                MathF.Max(0.0f, finalSize.Height - 2.0f * Padding)));
        }

        protected override void DrawCore(in UiDrawContext context)
        {
            var surface = new RoundedRectangle(
                new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height),
                _design.ControlCornerRadius,
                _design.ControlCornerRadius);
            context.FillRoundedRectangle(surface, context.Palette.Surface);
            context.DrawRoundedRectangle(surface, context.Palette.SurfaceBorder);
        }

        public void Dispose()
        {
            foreach (Button button in _buttons)
            {
                button.Dispose();
            }
        }
    }
}
