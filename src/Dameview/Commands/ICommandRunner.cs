namespace Dameview.Commands;

/// <summary>Runs commands for the UI, which has no host of its own to give them.</summary>
internal interface ICommandRunner
{
    /// <summary>What shortcuts and the command palette aim at: the active tab.</summary>
    public CommandContext ActiveContext { get; }

    public bool CanExecute(Command command, CommandContext context);

    public void Execute(Command command, CommandContext context);
}
