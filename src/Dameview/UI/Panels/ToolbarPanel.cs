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
    private const float PaneButtonWidthDips = 68.0f;

    private readonly Button _syncButton;
    private readonly Button _maximizeButton;

    internal ToolbarPanel(ICommandRunner commands, ViewerPane pane)
    {
        void Run(Command command) => commands.Execute(command, CommandContext.For(pane.ActiveTab));
        Button CommandButton(string text, Command command) =>
            new(text, () => Run(command)) { ToolTip = new(command.Label) };

        Button PaneButton(string icon, Command command) =>
            new(icon, () => Run(command), fontFamily: UiTypography.IconFontFamily, fontSize: 16.0f)
            {
                ToolTip = new(command.Label),
                IsVisible = false,
            };

        _syncButton = PaneButton(UiTypography.LinkIcon, AppCommands.ToggleViewportSync);
        _maximizeButton = PaneButton(UiTypography.FullScreenIcon, AppCommands.ToggleMaximizedPane);
        Button[] buttons =
        [
            CommandButton("←", AppCommands.PreviousImage),
            CommandButton("→", AppCommands.NextImage),
            CommandButton("Fit", AppCommands.FitImage),
            CommandButton("1:1", AppCommands.ShowActualSize),
            _syncButton,
            _maximizeButton,
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

    /// <summary>Shows the buttons that only mean something with other panes around.</summary>
    internal void SetPaneButtons(bool split, bool synced, bool maximized)
    {
        _syncButton.IsVisible = split;
        _syncButton.IsSelected = synced;
        _maximizeButton.IsVisible = split;
        _maximizeButton.IsSelected = maximized;
        _maximizeButton.Label = maximized ? UiTypography.BackToWindowIcon : UiTypography.FullScreenIcon;
        MaxWidth = split ? WidthDips + (2.0f * PaneButtonWidthDips) : WidthDips;
    }
}
