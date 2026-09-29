using System.Diagnostics.CodeAnalysis;
using Dameview.Win32.Input;

namespace Dameview.Commands;

internal sealed class ViewerKeyBindings : IEquatable<ViewerKeyBindings>
{
    internal static ViewerKeyBindings Defaults { get; } = new(AppCommands.All
        .Where(command => command.DefaultShortcuts.Count > 0)
        .ToDictionary(command => command, command => command.DefaultShortcuts.ToArray()));

    private readonly Dictionary<Command, ViewerCommandShortcut[]> _shortcuts;

    private ViewerKeyBindings(Dictionary<Command, ViewerCommandShortcut[]> shortcuts)
    {
        _shortcuts = shortcuts;
    }

    internal IReadOnlyList<ViewerCommandShortcut> GetShortcuts(Command command) =>
        _shortcuts.TryGetValue(command, out ViewerCommandShortcut[]? shortcuts) ? shortcuts : [];

    internal bool TryGetCommand(
        CommandScope scope,
        WindowKeyEvent input,
        [NotNullWhen(true)] out Command? command)
    {
        foreach ((Command candidate, ViewerCommandShortcut[] shortcuts) in _shortcuts)
        {
            if (candidate.Scope == scope
                && shortcuts.Any(shortcut => shortcut.Matches(input)))
            {
                command = candidate;
                return true;
            }
        }

        command = null;
        return false;
    }

    // Assigning a shortcut takes it from whichever command holds it in the same scope.
    internal ViewerKeyBindings WithShortcut(Command command, ViewerCommandShortcut shortcut)
    {
        CommandScope scope = command.Scope;
        Dictionary<Command, ViewerCommandShortcut[]> shortcuts = new(_shortcuts);
        foreach (Command holder in _shortcuts.Keys)
        {
            if (holder.Scope == scope)
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
        Command command,
        IEnumerable<ViewerCommandShortcut> shortcuts)
    {
        Dictionary<Command, ViewerCommandShortcut[]> updated = new(_shortcuts);
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

        foreach ((Command command, ViewerCommandShortcut[] shortcuts) in _shortcuts)
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
        foreach ((Command command, ViewerCommandShortcut[] shortcuts) in _shortcuts)
        {
            hash ^= HashCode.Combine(command, shortcuts.Length);
        }

        return hash;
    }
}
