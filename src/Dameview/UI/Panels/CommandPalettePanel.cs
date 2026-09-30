using System.Drawing;
using Dameview.Commands;
using Dameview.UI.Animation;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.Win32.Input;
using Vortice.Direct2D1;

namespace Dameview.UI.Panels;

internal sealed class CommandPalettePanel : ModalContent
{
    private readonly TextBlock _title;
    private readonly TextInput _filterInput;
    private readonly Func<Command, bool> _canExecute;
    private readonly StackPanel _itemList;
    private readonly ScrollView _list;
    private readonly TextBlock _emptyMessage;
    private readonly Action<ViewerKeyBindings> _applyKeyBindings;
    private ViewerKeyBindings _keyBindings;

    // In display order, which puts the commands that cannot run last.
    private CommandItem[] _items;
    private int _selectedIndex;

    internal CommandPalettePanel(
        IReadOnlyList<Command> commands,
        ViewerKeyBindings keyBindings,
        Action<Command> execute,
        Func<Command, bool> canExecute,
        Action<ViewerKeyBindings> applyKeyBindings)
    {
        _canExecute = canExecute;
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
                () => execute(command),
                (slot, shortcut) => RecordShortcut(command, slot, shortcut),
                slot => RemoveShortcut(command, slot))),
        ];
        _keyBindings = keyBindings;
        _applyKeyBindings = applyKeyBindings;
        _itemList = new StackPanel(UiOrientation.Vertical, _items)
        {
            Spacing = UiDesign.SmallSpacing,
        };
        _list = new ScrollView(_itemList);
        _emptyMessage = new TextBlock(
            "No matching commands.",
            UiTextStyle.Body,
            UiTextTone.Secondary,
            UiTextWrapping.NoWrap)
        {
            IsVisible = false,
        };

        var results = new Overlay(_list, _emptyMessage);
        AddChild(new StackPanel(UiOrientation.Vertical, _title, _filterInput, results)
        {
            Spacing = UiDesign.Spacing,
            Fill = results,
            Margin = new UiThickness(UiDesign.LargeSpacing),
        });
        RefreshShortcuts();
        ApplyFilter(string.Empty);
    }

    internal override SizeF PreferredSize => new(540.0f, 580.0f);
    internal override UiElement InitialFocus => _filterInput;
    internal int MatchingCommandCount => _items.Count(item => item.IsPresent);
    internal Command? SelectedCommand => GetSelectableItems().ElementAtOrDefault(_selectedIndex)?.Command;
    internal string Query => _filterInput.Text;

    internal bool IsRecording => Root?.KeyboardCaptor is ShortcutChip;

    internal void Reset()
    {
        if (Root is { KeyboardCaptor: ShortcutChip chip } root)
        {
            root.ReleaseKeyboard(chip);
        }

        // Commands that cannot run stay listed, so their shortcuts can still be edited.
        foreach (CommandItem item in _items)
        {
            item.IsEnabled = _canExecute(item.Command);
        }

        _items = [.. _items.OrderBy(item => !item.IsEnabled)];
        _itemList.Reorder(_items);

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
        CommandItem[] selectableItems = GetSelectableItems();
        if (selectableItems.Length == 0)
        {
            return;
        }

        _selectedIndex = (_selectedIndex + offset + selectableItems.Length) % selectableItems.Length;
        UpdateSelection(scrollIntoView: true);
    }

    private void ExecuteSelectedCommand()
    {
        CommandItem[] selectableItems = GetSelectableItems();
        if (_selectedIndex >= 0 && _selectedIndex < selectableItems.Length)
        {
            selectableItems[_selectedIndex].Execute();
        }
    }

    private void UpdateSelection(bool scrollIntoView)
    {
        CommandItem[] selectableItems = GetSelectableItems();
        CommandItem? selected = _selectedIndex >= 0 && _selectedIndex < selectableItems.Length
            ? selectableItems[_selectedIndex]
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

    private CommandItem[] GetSelectableItems() => [.. _items.Where(item => item.IsPresent && item.IsEnabled)];

    internal void ApplyKeyBindings(ViewerKeyBindings keyBindings)
    {
        _keyBindings = keyBindings;
        RefreshShortcuts();
    }

    private void RecordShortcut(Command command, int slot, ViewerCommandShortcut shortcut) =>
        Apply(_keyBindings
            .WithShortcuts(command, Without(_keyBindings.GetShortcuts(command), slot))
            .WithShortcut(command, shortcut));

    private void RemoveShortcut(Command command, int slot)
    {
        IReadOnlyList<ViewerCommandShortcut> existing = _keyBindings.GetShortcuts(command);
        if (slot < existing.Count)
        {
            Apply(_keyBindings.WithShortcuts(command, Without(existing, slot)));
        }
        else
        {
            // The chip being added was abandoned; the refresh drops it.
            RefreshShortcuts();
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
        RefreshShortcuts();
        _applyKeyBindings(bindings);
    }

    private void RefreshShortcuts()
    {
        foreach (CommandItem item in _items)
        {
            item.SetShortcuts(_keyBindings.GetShortcuts(item.Command));
        }

        InvalidateLayout();
    }

    private sealed class CommandItem : InteractiveControl
    {
        private const float RowHeight = 40.0f;
        private const float AddButtonWidth = 26.0f;

        private readonly Action _execute;
        private readonly Action<int, ViewerCommandShortcut> _recordShortcut;
        private readonly Action<int> _removeShortcut;
        private readonly TextBlock _label;
        private readonly StackPanel _chipRow;
        private readonly StackPanel _row;
        private readonly List<ShortcutChip> _chips = [];

        internal CommandItem(
            Command command,
            Action execute,
            Action<int, ViewerCommandShortcut> recordShortcut,
            Action<int> removeShortcut)
        {
            Command = command;
            _execute = execute;
            _recordShortcut = recordShortcut;
            _removeShortcut = removeShortcut;
            _label = new TextBlock(command.Label, UiTextStyle.Label, UiTextTone.Primary, UiTextWrapping.NoWrap)
            {
                VerticalAlignment = UiAlignment.Center,
            };
            _chipRow = new StackPanel(UiOrientation.Horizontal)
            {
                Spacing = UiDesign.SmallSpacing,
                VerticalAlignment = UiAlignment.Center,
            };
            var addButton = new Button("+", () => AddChip().BeginRecording())
            {
                MaxWidth = AddButtonWidth,
                MaxHeight = ShortcutChip.Height,
                VerticalAlignment = UiAlignment.Center,
            };
            _row = new StackPanel(UiOrientation.Horizontal, _label, _chipRow, addButton)
            {
                Spacing = UiDesign.SmallSpacing,
                Fill = _label,
                Margin = new UiThickness(12.0f, 0.0f),
            };
            AddChild(_row);
            // Items that stop matching the filter fade and collapse out of the list.
            Transition = new UiTransition(Fade: true, Collapse: true, Response: 25.0);
        }

        internal Command Command { get; }
        internal bool IsSelected
        {
            get => HasVisualState(UiVisualState.Selected);
            set => SetVisualState(UiVisualState.Selected, value);
        }
        internal override bool IsFocusable => false;
        internal override bool PreservesFocusOnPointerPress => true;

        internal void Execute() => _execute();

        // A chip at index i always stands for slot i, so only the count ever changes and
        // the callbacks are fixed when the chip is created. A chip being added by "+" sits
        // one past the bound ones, and goes when the shortcuts are next set without it.
        internal void SetShortcuts(IReadOnlyList<ViewerCommandShortcut> shortcuts)
        {
            while (_chips.Count > shortcuts.Count)
            {
                // Off the list first: removing a chip that is recording ends the recording,
                // which can set the shortcuts again.
                ShortcutChip chip = _chips[^1];
                _chips.RemoveAt(_chips.Count - 1);
                _chipRow.Remove(chip);
            }

            while (_chips.Count < shortcuts.Count)
            {
                AddChip();
            }

            for (int slot = 0; slot < shortcuts.Count; slot++)
            {
                _chips[slot].Shortcut = shortcuts[slot];
            }
        }

        private ShortcutChip AddChip()
        {
            int slot = _chips.Count;
            var chip = new ShortcutChip(
                shortcut => _recordShortcut(slot, shortcut),
                () => _removeShortcut(slot));
            _chips.Add(chip);
            _chipRow.Add(chip);
            return chip;
        }

        protected override SizeF MeasureCore(SizeF availableSize) =>
            new(_row.Measure(availableSize).Width, RowHeight);

        // The row keeps its full height while the item collapses, so the shrinking bounds clip it
        // from below instead of squashing it.
        protected override void ArrangeCore(SizeF finalSize) =>
            _row.Arrange(new RectangleF(0.0f, 0.0f, finalSize.Width, RowHeight));

        protected override void OnVisualStateChanged()
        {
            base.OnVisualStateChanged();
            _label.Tone = IsEnabled ? UiTextTone.Primary : UiTextTone.Secondary;
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
        }

        protected override void Activate() => Execute();
    }
}
