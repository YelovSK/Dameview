using System.Drawing;
using Dameview.Commands;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class CommandPalettePanel : ModalContent, IDisposable
{
    private const float Padding = 16.0f;
    private const float TitleHeight = 36.0f;
    private const float InputHeight = 38.0f;
    private const float SectionGap = 8.0f;

    private readonly TextBlock _title;
    private readonly TextInput _filterInput;
    private readonly CommandItem[] _items;
    private readonly ScrollView _list;
    private readonly TextBlock _emptyMessage;
    private int _selectedIndex;

    internal CommandPalettePanel(
        IDWriteFactory factory,
        IReadOnlyList<ViewerCommand> commands,
        Action<ViewerCommandId> execute)
    {
        ArgumentOutOfRangeException.ThrowIfZero(commands.Count);
        _title = new TextBlock(
            factory,
            "Commands",
            UiTextStyle.Heading,
            UiTextTone.Primary,
            UiTextWrapping.NoWrap);
        _filterInput = new TextInput(factory, "Filter commands");
        _filterInput.TextChanged += ApplyFilter;
        _items =
        [
            .. commands.Select(command => new CommandItem(
                factory,
                command,
                ViewerKeyBindings.GetPrimaryShortcut(command.Id),
                () => execute(command.Id))),
        ];
        var itemList = new StackPanel(
            UiOrientation.Vertical,
            UiDesign.SmallSpacing,
            StackPanelDistribution.Natural,
            _items);
        _list = new ScrollView(itemList);
        _emptyMessage = new TextBlock(
            factory,
            "No matching commands.",
            UiTextStyle.Body,
            UiTextTone.Secondary,
            UiTextWrapping.NoWrap)
        {
            IsVisible = false,
        };

        AddChild(_title);
        AddChild(_filterInput);
        AddChild(_list);
        AddChild(_emptyMessage);
        ApplyFilter(string.Empty);
    }

    internal override SizeF PreferredSize => new(540.0f, 580.0f);
    internal override UiElement InitialFocus => _filterInput;
    internal int VisibleCommandCount => _items.Count(item => item.IsVisible);
    internal ViewerCommandId? SelectedCommand => GetVisibleItems().ElementAtOrDefault(_selectedIndex)?.Command.Id;
    internal string Query => _filterInput.Text;

    internal void Reset()
    {
        if (_filterInput.Text.Length > 0)
        {
            _filterInput.Clear();
        }
        else
        {
            ApplyFilter(string.Empty);
        }
    }

    internal override bool OnKeyEvent(WindowKeyEvent input)
    {
        switch (input.Key)
        {
            case WindowKey.Up:
                MoveSelection(-1);
                return true;

            case WindowKey.Down:
                MoveSelection(1);
                return true;

            case WindowKey.Enter:
                ExecuteSelectedCommand();
                return true;

            default:
                return false;
        }
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float contentWidth = MathF.Max(0.0f, availableSize.Width - (2.0f * Padding));
        _title.Measure(new SizeF(contentWidth, TitleHeight));
        _filterInput.Measure(new SizeF(contentWidth, InputHeight));
        _list.Measure(new SizeF(contentWidth, CalculateListHeight(availableSize.Height)));
        _emptyMessage.Measure(new SizeF(contentWidth, CalculateListHeight(availableSize.Height)));
        return PreferredSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        float contentWidth = MathF.Max(0.0f, finalSize.Width - (2.0f * Padding));
        _title.Arrange(new RectangleF(Padding, Padding, contentWidth, TitleHeight));
        _filterInput.Arrange(new RectangleF(
            Padding,
            Padding + TitleHeight + SectionGap,
            contentWidth,
            InputHeight));
        float listTop = Padding + TitleHeight + SectionGap + InputHeight + SectionGap;
        float listHeight = CalculateListHeight(finalSize.Height);
        _list.Arrange(new RectangleF(
            Padding,
            listTop,
            contentWidth,
            listHeight));
        _emptyMessage.Arrange(new RectangleF(Padding, listTop, contentWidth, listHeight));
    }

    public void Dispose()
    {
        _title.Dispose();
        _filterInput.Dispose();
        _emptyMessage.Dispose();
        foreach (CommandItem item in _items)
        {
            item.Dispose();
        }
    }

    private static float CalculateListHeight(float panelHeight) =>
        MathF.Max(
            0.0f,
            panelHeight - (2.0f * Padding) - TitleHeight - InputHeight - (2.0f * SectionGap));

    private void ApplyFilter(string query)
    {
        foreach (CommandItem item in _items)
        {
            item.IsVisible = item.Command.Label.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        _selectedIndex = 0;
        _list.SetScrollOffset(0.0f);
        _emptyMessage.IsVisible = VisibleCommandCount == 0;
        UpdateSelection(scrollIntoView: false);
    }

    private void MoveSelection(int offset)
    {
        CommandItem[] visibleItems = GetVisibleItems();
        if (visibleItems.Length == 0)
        {
            return;
        }

        _selectedIndex = (_selectedIndex + offset + visibleItems.Length) % visibleItems.Length;
        UpdateSelection(scrollIntoView: true);
    }

    private void ExecuteSelectedCommand()
    {
        CommandItem[] visibleItems = GetVisibleItems();
        if (_selectedIndex >= 0 && _selectedIndex < visibleItems.Length)
        {
            visibleItems[_selectedIndex].Execute();
        }
    }

    private void UpdateSelection(bool scrollIntoView)
    {
        CommandItem[] visibleItems = GetVisibleItems();
        CommandItem? selected = _selectedIndex >= 0 && _selectedIndex < visibleItems.Length
            ? visibleItems[_selectedIndex]
            : null;
        foreach (CommandItem item in _items)
        {
            item.IsSelected = ReferenceEquals(item, selected);
        }

        if (scrollIntoView && selected is not null && _list.Bounds.Height > 0.0f)
        {
            _list.BringIntoView(selected.GetBoundsRelativeTo(_list));
        }
    }

    private CommandItem[] GetVisibleItems() => [.. _items.Where(item => item.IsVisible)];

    private sealed class CommandItem : InteractiveControl, IDisposable
    {
        private readonly Action _execute;
        private readonly IDWriteTextFormat _labelFormat;
        private readonly IDWriteTextFormat _shortcutFormat;

        internal CommandItem(
            IDWriteFactory factory,
            ViewerCommand command,
            ViewerCommandShortcut? shortcut,
            Action execute)
        {
            Command = command;
            Shortcut = shortcut;
            _execute = execute;
            _labelFormat = CreateFormat(factory, TextAlignment.Leading, FontWeight.Medium);
            _shortcutFormat = CreateFormat(factory, TextAlignment.Trailing, FontWeight.Normal);
        }

        internal ViewerCommand Command { get; }
        internal ViewerCommandShortcut? Shortcut { get; }
        internal bool IsSelected
        {
            get => HasVisualState(UiVisualState.Selected);
            set => SetVisualState(UiVisualState.Selected, value);
        }
        internal override bool IsFocusable => false;
        internal override bool PreservesFocusOnPointerPress => true;

        internal void Execute() => _execute();

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            float width = float.IsFinite(availableSize.Width) ? availableSize.Width : 320.0f;
            return new SizeF(MathF.Max(0.0f, width), 40.0f);
        }

        protected override void DrawCore(in UiDrawContext context)
        {
            var background = new RoundedRectangle(
                new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height),
                UiDesign.ControlCornerRadius,
                UiDesign.ControlCornerRadius);
            if (IsSelected)
            {
                context.FillRoundedRectangle(background, context.Palette.Accent, 0.18f);
                context.DrawRoundedRectangle(background, context.Palette.Accent);
            }
            else if (HoverAmount > 0.0f)
            {
                context.FillRoundedRectangle(background, context.Palette.ControlHover, HoverAmount);
            }

            if (PressedAmount > 0.0f)
            {
                context.FillRoundedRectangle(background, context.Palette.ControlPressed, PressedAmount);
            }

            const float horizontalPadding = 12.0f;
            const float shortcutWidth = 140.0f;
            float labelRight = MathF.Max(horizontalPadding, Bounds.Width - shortcutWidth);
            context.DrawText(
                Command.Label,
                _labelFormat,
                new Rect(horizontalPadding, 0.0f, labelRight, Bounds.Height),
                context.Palette.PrimaryText,
                DrawTextOptions.Clip);
            if (Shortcut is { } shortcut)
            {
                context.DrawText(
                    shortcut.DisplayText,
                    _shortcutFormat,
                    new Rect(
                        labelRight,
                        0.0f,
                        MathF.Max(0.0f, Bounds.Width - horizontalPadding - labelRight),
                        Bounds.Height),
                    context.Palette.SecondaryText,
                    DrawTextOptions.Clip);
            }
        }

        public void Dispose()
        {
            _labelFormat.Dispose();
            _shortcutFormat.Dispose();
        }

        protected override void Activate() => Execute();

        private static IDWriteTextFormat CreateFormat(
            IDWriteFactory factory,
            TextAlignment alignment,
            FontWeight weight)
        {
            IDWriteTextFormat format = factory.CreateTextFormat(
                UiTypography.FontFamily,
                weight,
                FontStyle.Normal,
                UiDesign.BodyFontSize);
            format.TextAlignment = alignment;
            format.ParagraphAlignment = ParagraphAlignment.Center;
            format.WordWrapping = WordWrapping.NoWrap;
            return format;
        }
    }
}
