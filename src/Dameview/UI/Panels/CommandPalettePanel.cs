using System.Drawing;
using Dameview.Commands;
using Dameview.UI.Animation;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI.Panels;

internal sealed class CommandPalettePanel : ModalContent
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
    private readonly Action<ViewerKeyBindings> _applyKeyBindings;
    private ViewerKeyBindings _keyBindings;
    private (ViewerCommandId Command, int Slot)? _capture;
    private int _selectedIndex;

    internal CommandPalettePanel(
        IReadOnlyList<ViewerCommand> commands,
        ViewerKeyBindings keyBindings,
        Action<ViewerCommandId> execute,
        Action<ViewerKeyBindings> applyKeyBindings)
    {
        ArgumentOutOfRangeException.ThrowIfZero(commands.Count);
        _title = new TextBlock(
            "Commands",
            UiTextStyle.Heading,
            UiTextTone.Primary,
            UiTextWrapping.NoWrap);
        _filterInput = new TextInput("Filter commands");
        _filterInput.TextChanged += ApplyFilter;
        _items =
        [
            .. commands.Select(command => new CommandItem(
                command,
                () => execute(command.Id),
                slot => BeginCapture(command.Id, slot),
                slot => RemoveShortcut(command.Id, slot))),
        ];
        _keyBindings = keyBindings;
        _applyKeyBindings = applyKeyBindings;
        var itemList = new StackPanel(
            UiOrientation.Vertical,
            UiDesign.SmallSpacing,
            StackPanelDistribution.Natural,
            _items);
        _list = new ScrollView(itemList);
        _emptyMessage = new TextBlock(
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
        RefreshShortcuts();
        ApplyFilter(string.Empty);
    }

    internal override SizeF PreferredSize => new(540.0f, 580.0f);
    internal override UiElement InitialFocus => _filterInput;
    internal int MatchingCommandCount => _items.Count(item => item.IsPresent);
    internal ViewerCommandId? SelectedCommand => GetMatchingItems().ElementAtOrDefault(_selectedIndex)?.Command.Id;
    internal string Query => _filterInput.Text;

    internal bool IsCapturing => _capture is not null;

    internal void Reset()
    {
        EndCapture();
        if (_filterInput.Text.Length > 0)
        {
            _filterInput.Clear();
        }
        else
        {
            ApplyFilter(string.Empty);
        }

        // The palette opens on the full list rather than animating it back in.
        foreach (CommandItem item in _items)
        {
            item.FinishTransition();
        }
    }

    internal override bool OnKeyEvent(WindowKeyEvent input)
    {
        // While recording, the palette holds the keyboard, so a shortcut cannot fire the
        // command it is being bound to.
        if (_capture is (ViewerCommandId command, int slot))
        {
            RecordShortcut(input, command, slot);
            return true;
        }

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

    private static float CalculateListHeight(float panelHeight) =>
        MathF.Max(
            0.0f,
            panelHeight - (2.0f * Padding) - TitleHeight - InputHeight - (2.0f * SectionGap));

    private void ApplyFilter(string query)
    {
        foreach (CommandItem item in _items)
        {
            item.IsPresent = item.Command.Label.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        _selectedIndex = 0;
        _list.SetScrollOffset(0.0f);
        _emptyMessage.IsVisible = MatchingCommandCount == 0;
        UpdateSelection(scrollIntoView: false);
    }

    private void MoveSelection(int offset)
    {
        CommandItem[] matchingItems = GetMatchingItems();
        if (matchingItems.Length == 0)
        {
            return;
        }

        _selectedIndex = (_selectedIndex + offset + matchingItems.Length) % matchingItems.Length;
        UpdateSelection(scrollIntoView: true);
    }

    private void ExecuteSelectedCommand()
    {
        CommandItem[] matchingItems = GetMatchingItems();
        if (_selectedIndex >= 0 && _selectedIndex < matchingItems.Length)
        {
            matchingItems[_selectedIndex].Execute();
        }
    }

    private void UpdateSelection(bool scrollIntoView)
    {
        CommandItem[] matchingItems = GetMatchingItems();
        CommandItem? selected = _selectedIndex >= 0 && _selectedIndex < matchingItems.Length
            ? matchingItems[_selectedIndex]
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

    private CommandItem[] GetMatchingItems() => [.. _items.Where(item => item.IsPresent)];

    internal void ApplyKeyBindings(ViewerKeyBindings keyBindings)
    {
        _keyBindings = keyBindings;
        RefreshShortcuts();
    }

    internal override void OnKeyboardCaptureLost()
    {
        _capture = null;
        RefreshShortcuts();
    }

    private void BeginCapture(ViewerCommandId command, int slot)
    {
        _capture = (command, slot);
        Root?.CaptureKeyboard(this);
        RefreshShortcuts();
    }

    private void EndCapture() => Root?.ReleaseKeyboard(this);

    private void RecordShortcut(WindowKeyEvent input, ViewerCommandId command, int slot)
    {
        switch (input.Key)
        {
            case WindowKey.Escape:
                EndCapture();
                break;

            case WindowKey.Delete or WindowKey.Backspace:
                RemoveShortcut(command, slot);
                EndCapture();
                break;

            // Keys WindowKey does not name, such as a bare modifier, have no text form and so
            // could not be written to settings.
            case var key when !Enum.IsDefined(key):
                break;

            default:
                Apply(_keyBindings
                    .WithShortcuts(command, Without(_keyBindings.GetShortcuts(command), slot))
                    .WithShortcut(command, new ViewerCommandShortcut(input.Key, input.Control, input.Shift)));
                break;
        }
    }

    private void RemoveShortcut(ViewerCommandId command, int slot)
    {
        IReadOnlyList<ViewerCommandShortcut> existing = _keyBindings.GetShortcuts(command);
        if (slot >= 0 && slot < existing.Count)
        {
            Apply(_keyBindings.WithShortcuts(command, Without(existing, slot)));
        }
    }

    // A slot outside the list is the pending one added by "+", which drops nothing.
    private static IEnumerable<ViewerCommandShortcut> Without(
        IReadOnlyList<ViewerCommandShortcut> shortcuts,
        int slot) =>
        shortcuts.Where((_, index) => index != slot);

    private void Apply(ViewerKeyBindings bindings)
    {
        _keyBindings = bindings;
        EndCapture();
        RefreshShortcuts();
        _applyKeyBindings(bindings);
    }

    private void RefreshShortcuts()
    {
        foreach (CommandItem item in _items)
        {
            item.SetShortcuts(
                _keyBindings.GetShortcuts(item.Command.Id),
                _capture is (ViewerCommandId command, int slot) && command == item.Command.Id
                    ? slot
                    : CommandItem.NoCaptureSlot);
        }

        InvalidateLayout();
    }

    private sealed class CommandItem : InteractiveControl
    {
        internal const int NoCaptureSlot = -1;

        private const float ChipGap = 4.0f;
        private const float AddButtonWidth = 26.0f;
        private const float ChipPadding = 12.0f;
        private const string CaptureLabel = "Press a key";
        private const float RowHeight = 40.0f;
        private static readonly UiFont LabelFont = new(UiDesign.BodyFontSize, FontWeight.Medium);

        private readonly Action _execute;
        private readonly Action<int> _captureShortcut;
        private readonly Action<int> _removeShortcut;
        private readonly Button _addButton;
        private readonly List<ShortcutChip> _chips = [];

        internal CommandItem(
            ViewerCommand command,
            Action execute,
            Action<int> captureShortcut,
            Action<int> removeShortcut)
        {
            Command = command;
            _execute = execute;
            _captureShortcut = captureShortcut;
            _removeShortcut = removeShortcut;
            _addButton = new Button("+", () => _captureShortcut(_chips.Count));
            AddChild(_addButton);
            // Items that stop matching the filter fade and collapse out of the list.
            Transition = new UiTransition(Fade: true, Collapse: true, Response: 25.0);
        }

        internal ViewerCommand Command { get; }
        private float ChipsLeft { get; set; }
        internal bool IsSelected
        {
            get => HasVisualState(UiVisualState.Selected);
            set => SetVisualState(UiVisualState.Selected, value);
        }
        internal override bool IsFocusable => false;
        internal override bool PreservesFocusOnPointerPress => true;

        internal void Execute() => _execute();

        // A chip at index i always stands for slot i, so only the count ever changes and
        // the callbacks are fixed when the chip is created.
        internal void SetShortcuts(IReadOnlyList<ViewerCommandShortcut> shortcuts, int capturingSlot)
        {
            // The slot being added by "+" sits one past the bound ones.
            int count = shortcuts.Count + (capturingSlot >= shortcuts.Count ? 1 : 0);
            while (_chips.Count > count)
            {
                RemoveChild(_chips[^1]);
                _chips.RemoveAt(_chips.Count - 1);
            }

            while (_chips.Count < count)
            {
                int slot = _chips.Count;
                var chip = new ShortcutChip(
                    CaptureLabel,
                    () => _captureShortcut(slot),
                    () => _removeShortcut(slot));
                _chips.Add(chip);
                AddChild(chip);
            }

            for (int slot = 0; slot < count; slot++)
            {
                bool pending = slot >= shortcuts.Count;
                _chips[slot].Label = pending || slot == capturingSlot
                    ? CaptureLabel
                    : shortcuts[slot].Text;
                _chips[slot].CanRemove = !pending && slot == capturingSlot;
            }

            _addButton.IsVisible = capturingSlot == NoCaptureSlot;
            InvalidateLayout();
        }

        protected override SizeF MeasureCore(SizeF availableSize)
        {
            float width = float.IsFinite(availableSize.Width) ? availableSize.Width : 320.0f;
            foreach (ShortcutChip chip in _chips)
            {
                chip.Measure(new SizeF(width, ShortcutChip.Height));
            }

            _addButton.Measure(new SizeF(AddButtonWidth, ShortcutChip.Height));
            return new SizeF(MathF.Max(0.0f, width), RowHeight);
        }

        // The row keeps its full height while the item collapses, so the shrinking bounds clip it
        // from below instead of squashing it.
        protected override void ArrangeCore(SizeF finalSize)
        {
            float top = (RowHeight - ShortcutChip.Height) / 2.0f;
            float right = finalSize.Width - ChipPadding;
            if (_addButton.IsVisible)
            {
                right -= AddButtonWidth;
                _addButton.Arrange(new RectangleF(right, top, AddButtonWidth, ShortcutChip.Height));
                right -= ChipGap;
            }

            for (int index = _chips.Count - 1; index >= 0; index--)
            {
                float width = _chips[index].DesiredSize.Width;
                right -= width;
                _chips[index].Arrange(new RectangleF(right, top, width, ShortcutChip.Height));
                right -= ChipGap;
            }

            ChipsLeft = right;
        }

        protected override void DrawCore(in UiDrawContext context)
        {
            var background = new RoundedRectangle(
                new RectangleF(0.0f, 0.0f, Bounds.Width, RowHeight),
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

            context.DrawText(
                Command.Label,
                LabelFont,
                new Rect(
                    ChipPadding,
                    0.0f,
                    MathF.Max(ChipPadding, ChipsLeft - ChipGap),
                    RowHeight),
                context.Palette.PrimaryText,
                DrawTextOptions.Clip);
        }

        protected override void Activate() => Execute();
    }
}
