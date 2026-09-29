using System.Drawing;
using Dameview.Viewing;

namespace Dameview.Commands;

// Window commands run before the UI sees the key.
internal enum CommandScope
{
    Window,
    Viewer,
}

/// <summary>What a command is aimed at.</summary>
/// <remarks>
/// Shortcuts and the command palette aim at the active tab, while a context menu aims at whatever
/// was clicked. It holds targets only; what a command works through comes from its host.
/// </remarks>
internal sealed record CommandContext(ViewerTab? Tab, string? ImagePath, PointF? Anchor = null)
{
    /// <summary>A tab and the image it shows.</summary>
    /// <param name="anchor">Where zooming centers, in the tab's viewport pixels.</param>
    internal static CommandContext For(ViewerTab tab, PointF? anchor = null) =>
        new(tab, tab.Session.State.DisplayedImage?.Path, anchor);

    /// <summary>An image file that no tab needs to be showing, such as a gallery item.</summary>
    internal static CommandContext ForFile(string path) => new(null, path);
}

/// <summary>A named action that shortcuts, the command palette, buttons, and menus all invoke.</summary>
/// <remarks>
/// What a command needs from its context is decided by the factory that creates it, so its
/// handler receives that already, and a context without it leaves the command unable to run.
/// </remarks>
internal sealed class Command
{
    private readonly Func<ICommandHost, CommandContext, bool> _canExecute;
    private readonly Action<ICommandHost, CommandContext> _execute;

    private Command(
        string id,
        string label,
        CommandScope scope,
        ViewerCommandShortcut[]? shortcuts,
        Func<ICommandHost, CommandContext, bool> canExecute,
        Action<ICommandHost, CommandContext> execute)
    {
        Id = id;
        Label = label;
        Scope = scope;
        DefaultShortcuts = shortcuts ?? [];
        _canExecute = canExecute;
        _execute = execute;
    }

    /// <summary>Names the command in the settings file, so it must never change.</summary>
    internal string Id { get; }
    internal string Label { get; }
    internal CommandScope Scope { get; }
    internal IReadOnlyList<ViewerCommandShortcut> DefaultShortcuts { get; }

    internal bool CanExecute(ICommandHost host, CommandContext context) => _canExecute(host, context);

    // Whether a command can run depends on state that changes on its own, like an image that
    // failed to load after its menu opened, so being unable to is not a caller's mistake.
    internal void Execute(ICommandHost host, CommandContext context)
    {
        if (CanExecute(host, context))
        {
            _execute(host, context);
        }
    }

    internal static Command Global(
        string id,
        string label,
        CommandScope scope,
        Action<ICommandHost> execute,
        Func<ICommandHost, bool>? canExecute = null,
        ViewerCommandShortcut[]? shortcuts = null) =>
        new(
            id,
            label,
            scope,
            shortcuts,
            (host, _) => canExecute?.Invoke(host) ?? true,
            (host, _) => execute(host));

    internal static Command ForTab(
        string id,
        string label,
        CommandScope scope,
        Action<ICommandHost, ViewerTab> execute,
        Func<ViewerTab, bool>? canExecute = null,
        ViewerCommandShortcut[]? shortcuts = null) =>
        ForTab(id, label, scope, (host, tab, _) => execute(host, tab), canExecute, shortcuts);

    /// <summary>A tab command that zooms, at the context's anchor or else the viewport center.</summary>
    internal static Command ForTab(
        string id,
        string label,
        CommandScope scope,
        Action<ICommandHost, ViewerTab, PointF> execute,
        Func<ViewerTab, bool>? canExecute = null,
        ViewerCommandShortcut[]? shortcuts = null) =>
        new(
            id,
            label,
            scope,
            shortcuts,
            (_, context) => context.Tab is { } tab && (canExecute?.Invoke(tab) ?? true),
            (host, context) => execute(
                host,
                context.Tab!,
                context.Anchor ?? context.Tab!.Session.Viewport.ViewportCenter));

    internal static Command ForImage(
        string id,
        string label,
        CommandScope scope,
        Action<ICommandHost, string> execute,
        ViewerCommandShortcut[]? shortcuts = null) =>
        new(
            id,
            label,
            scope,
            shortcuts,
            (_, context) => context.ImagePath is not null,
            (host, context) => execute(host, context.ImagePath!));
}
