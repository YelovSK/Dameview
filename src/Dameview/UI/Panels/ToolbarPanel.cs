using System.Drawing;
using Dameview.Commands;
using Dameview.UI.Animation;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.Viewing;

namespace Dameview.UI.Panels;

internal sealed class ToolbarPanel : UiElement
{
    internal const float HeightDips = 46.0f;
    private const float WidthDips = 342.0f;
    private const float SyncButtonWidthDips = 68.0f;

    private readonly Button _syncButton;

    internal ToolbarPanel(ICommandRunner commands, ViewerPane pane)
    {
        void Run(Command command) => commands.Execute(command, CommandContext.For(pane.ActiveTab));
        Button CommandButton(string text, Command command) =>
            new(text, () => Run(command)) { ToolTip = new(command.Label) };

        _syncButton = new Button(
            UiTypography.LinkIcon,
            () => Run(AppCommands.ToggleViewportSync),
            fontFamily: UiTypography.IconFontFamily,
            fontSize: 16.0f)
        {
            ToolTip = new(AppCommands.ToggleViewportSync.Label),
            IsVisible = false,
        };
        Button[] buttons =
        [
            CommandButton("←", AppCommands.PreviousImage),
            CommandButton("→", AppCommands.NextImage),
            CommandButton("Fit", AppCommands.FitImage),
            CommandButton("1:1", AppCommands.ShowActualSize),
            _syncButton,
            new Button(
                UiTypography.SettingsIcon,
                () => Run(AppCommands.ShowSettings),
                fontFamily: UiTypography.IconFontFamily,
                fontSize: 16.0f)
            {
                ToolTip = new("Settings"),
            },
        ];
        var buttonRow = new StackPanel(UiOrientation.Horizontal, buttons)
        {
            Spacing = UiDesign.SmallSpacing,
            Distribution = StackPanelDistribution.Equal,
            Margin = new UiThickness(6.0f),
        };
        AddChild(new Surface(buttonRow)
        {
            Fill = UiSurfaceFill.Overlay,
        });
        MaxWidth = WidthDips;
        MaxHeight = HeightDips;
        Transition = new UiTransition(Fade: true, HiddenOffset: new PointF(0.0f, -HeightDips), Response: 14.0);
        IsPresent = false;
    }

    /// <param name="available">Whether there are other panes to sync with.</param>
    internal void SetViewportSync(bool available, bool synced)
    {
        _syncButton.IsVisible = available;
        _syncButton.IsSelected = synced;
        MaxWidth = available ? WidthDips + SyncButtonWidthDips : WidthDips;
    }
}
