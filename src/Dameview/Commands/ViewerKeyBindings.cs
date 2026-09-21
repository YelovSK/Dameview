using Dameview.Win32.Input;

namespace Dameview.Commands;

internal sealed class ViewerKeyBindings : IEquatable<ViewerKeyBindings>
{
    internal static ViewerKeyBindings Defaults { get; } = new(new Dictionary<ViewerCommandId, ViewerCommandShortcut[]>
    {
        [ViewerCommandId.OpenFile] = [new(WindowKey.O, Control: true)],
        [ViewerCommandId.NewTab] = [new(WindowKey.T, Control: true)],
        [ViewerCommandId.CloseTab] = [new(WindowKey.W, Control: true)],
        [ViewerCommandId.ReopenClosedTab] = [new(WindowKey.T, Control: true, Shift: true)],
        [ViewerCommandId.PreviousTab] = [new(WindowKey.Tab, Control: true, Shift: true)],
        [ViewerCommandId.NextTab] = [new(WindowKey.Tab, Control: true)],
        [ViewerCommandId.PreviousImage] = [new(WindowKey.Left)],
        [ViewerCommandId.NextImage] = [new(WindowKey.Right)],
        [ViewerCommandId.FitImage] = [new(WindowKey.F)],
        [ViewerCommandId.ShowActualSize] = [new(WindowKey.Number1), new(WindowKey.Numpad1)],
        [ViewerCommandId.ToggleFitActualSize] = [new(WindowKey.Z)],
        [ViewerCommandId.CopyImage] = [new(WindowKey.C, Control: true)],
        [ViewerCommandId.ToggleFullscreen] = [new(WindowKey.F11), new(WindowKey.F, Control: true)],
        [ViewerCommandId.SplitRight] = [new(WindowKey.S, Control: true, Shift: true)],
        [ViewerCommandId.SplitDown] = [new(WindowKey.S, Control: true)],
        [ViewerCommandId.TogglePerformanceOverlay] = [new(WindowKey.F3)],
        [ViewerCommandId.ShowSettings] = [new(WindowKey.Comma, Control: true)],
        [ViewerCommandId.ShowCommandPalette] = [new(WindowKey.P, Control: true, Shift: true)],
    });

    private readonly Dictionary<ViewerCommandId, ViewerCommandShortcut[]> _shortcuts;

    private ViewerKeyBindings(Dictionary<ViewerCommandId, ViewerCommandShortcut[]> shortcuts)
    {
        _shortcuts = shortcuts;
    }

    internal IReadOnlyList<ViewerCommandShortcut> GetShortcuts(ViewerCommandId command) =>
        _shortcuts.TryGetValue(command, out ViewerCommandShortcut[]? shortcuts) ? shortcuts : [];

    internal bool TryGetCommand(
        ViewerCommandScope scope,
        WindowKeyEvent input,
        out ViewerCommandId command)
    {
        foreach ((ViewerCommandId candidate, ViewerCommandShortcut[] shortcuts) in _shortcuts)
        {
            if (ViewerCommandCatalog.GetScope(candidate) == scope
                && shortcuts.Any(shortcut => shortcut.Matches(input)))
            {
                command = candidate;
                return true;
            }
        }

        command = default;
        return false;
    }

    // Assigning a shortcut takes it from whichever command holds it in the same scope.
    internal ViewerKeyBindings WithShortcut(ViewerCommandId command, ViewerCommandShortcut shortcut)
    {
        ViewerCommandScope scope = ViewerCommandCatalog.GetScope(command);
        Dictionary<ViewerCommandId, ViewerCommandShortcut[]> shortcuts = new(_shortcuts);
        foreach (ViewerCommandId holder in _shortcuts.Keys)
        {
            if (ViewerCommandCatalog.GetScope(holder) == scope)
            {
                shortcuts[holder] = [.. shortcuts[holder].Where(existing => existing != shortcut)];
            }
        }

        // From the copy, not the original.
        // The loop above has already taken this chord off
        // whoever held it, and that can include the command being assigned it.
        ViewerCommandShortcut[] existing =
            shortcuts.TryGetValue(command, out ViewerCommandShortcut[]? current) ? current : [];
        shortcuts[command] = [.. existing, shortcut];
        return new ViewerKeyBindings(shortcuts);
    }

    internal ViewerKeyBindings WithShortcuts(
        ViewerCommandId command,
        IEnumerable<ViewerCommandShortcut> shortcuts)
    {
        Dictionary<ViewerCommandId, ViewerCommandShortcut[]> updated = new(_shortcuts);
        ViewerCommandShortcut[] assigned = [.. shortcuts];

        // An unbound command holds no entry, so that it compares equal to one that was
        // never given an entry in the first place.
        if (assigned.Length == 0)
        {
            updated.Remove(command);
        }
        else
        {
            updated[command] = assigned;
        }

        return new ViewerKeyBindings(updated);
    }

    public bool Equals(ViewerKeyBindings? other)
    {
        if (other is null || _shortcuts.Count != other._shortcuts.Count)
        {
            return false;
        }

        foreach ((ViewerCommandId command, ViewerCommandShortcut[] shortcuts) in _shortcuts)
        {
            if (!other._shortcuts.TryGetValue(command, out ViewerCommandShortcut[]? others)
                || !shortcuts.AsSpan().SequenceEqual(others))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as ViewerKeyBindings);

    public override int GetHashCode()
    {
        // Order-independent so it matches Equals, which does not care about command order.
        int hash = _shortcuts.Count;
        foreach ((ViewerCommandId command, ViewerCommandShortcut[] shortcuts) in _shortcuts)
        {
            hash ^= HashCode.Combine(command, shortcuts.Length);
        }

        return hash;
    }
}
