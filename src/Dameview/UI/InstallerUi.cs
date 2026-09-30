using System.Drawing;
using Dameview.Installation;
using Dameview.Settings;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Dameview.UI;

internal sealed class InstallerUi : UiElement, IDisposable
{
    private readonly UiHost _host;
    private readonly AppInstallationRequest _request;
    private readonly TextBlock _title;
    private readonly TextBlock _description;
    private readonly TextBlock _location;
    private readonly TextBlock _status;
    private readonly Button _primaryButton;
    private readonly Button _secondaryButton;
    private readonly Button _uninstallButton;

    internal InstallerUi(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        AppInstallationRequest request,
        float dpi,
        Action primaryAction,
        Action secondaryAction,
        Action uninstall)
    {
        _host = new UiHost(this, deviceContext, directWriteFactory, dpi, Themes.Dark.Palette);
        _request = request;
        (string title, string description) = GetCopy(request);
        _title = new TextBlock(
            title,
            UiTextStyle.Heading,
            UiTextTone.Primary,
            UiTextWrapping.NoWrap);
        _description = new TextBlock(
            description,
            UiTextStyle.Body,
            UiTextTone.Secondary,
            UiTextWrapping.Wrap);
        _location = new TextBlock(
            request.Action == AppInstallationAction.Uninstall
                ? $"Installed in {AppInstallation.InstallDirectory}"
                : $"Install to {AppInstallation.InstallDirectory}",
            UiTextStyle.Body,
            UiTextTone.Secondary,
            UiTextWrapping.NoWrap);
        _status = new TextBlock(
            string.Empty,
            UiTextStyle.Body,
            UiTextTone.Error,
            UiTextWrapping.Wrap)
        {
            IsVisible = false,
        };
        _primaryButton = new Button(GetPrimaryLabel(request.Action), primaryAction)
        {
            IsSelected = true,
        };
        _secondaryButton = new Button(
            request.Action == AppInstallationAction.Uninstall ? "Cancel" : "Run Portable",
            secondaryAction);
        _uninstallButton = new Button(
            "Uninstall",
            uninstall,
            tone: UiButtonTone.Danger)
        {
            IsVisible = request.Action is AppInstallationAction.Update or AppInstallationAction.Reinstall,
            MaxWidth = 140.0f,
        };

        // Takes the slack even while the status is hidden, which keeps the buttons at the bottom.
        var statusArea = new Overlay(_status);
        var buttons = new StackPanel(UiOrientation.Horizontal, _primaryButton, _secondaryButton)
        {
            Spacing = UiDesign.Spacing,
            Distribution = StackPanelDistribution.Equal,
        };
        var content = new StackPanel(
            UiOrientation.Vertical,
            _title, _description, _location, statusArea, buttons, _uninstallButton)
        {
            Spacing = UiDesign.Spacing,
            Fill = statusArea,
            Margin = new UiThickness(24.0f),
        };

        AddChild(new Surface(content)
        {
            MaxWidth = 500.0f,
            MaxHeight = 320.0f,
            Margin = new UiThickness(UiDesign.WindowMargin),
        });
        _host.Root.SetFocus(_primaryButton);
    }

    internal event Action? Invalidated
    {
        add => _host.Root.Invalidated += value;
        remove => _host.Root.Invalidated -= value;
    }

    internal event Action<WindowCursor>? CursorChanged
    {
        add => _host.Root.CursorChanged += value;
        remove => _host.Root.CursorChanged -= value;
    }

    internal UiTheme Palette => _host.Palette;

    internal void ShowError(string message)
    {
        _status.Text = message;
        _status.Tone = UiTextTone.Error;
        _status.IsVisible = true;
        _host.Root.InvalidateVisual();
    }

    internal void ShowUninstallConfirmation()
    {
        _title.Text = "Uninstall Dameview";
        _description.Text = "Remove Dameview from this account. Your settings will be kept.";
        _location.Text = $"Installed in {AppInstallation.InstallDirectory}";
        _location.IsVisible = true;
        _status.IsVisible = false;
        _primaryButton.Label = "Uninstall";
        _secondaryButton.Label = "Back";
        _secondaryButton.IsVisible = true;
        _uninstallButton.IsVisible = false;
        _host.Root.SetFocus(_primaryButton);
        _host.Root.InvalidateLayout();
    }

    internal void ShowInstallationActions()
    {
        (string title, string description) = GetCopy(_request);
        _title.Text = title;
        _description.Text = description;
        _location.Text = $"Install to {AppInstallation.InstallDirectory}";
        _location.IsVisible = true;
        _status.IsVisible = false;
        _primaryButton.Label = GetPrimaryLabel(_request.Action);
        _secondaryButton.Label = "Run Portable";
        _secondaryButton.IsVisible = true;
        _uninstallButton.IsVisible = true;
        _host.Root.SetFocus(_primaryButton);
        _host.Root.InvalidateLayout();
    }

    internal void ShowUninstallComplete()
    {
        _description.Text = "Dameview was uninstalled. Your settings were kept.";
        _location.IsVisible = false;
        _status.IsVisible = false;
        _primaryButton.Label = "Close";
        _secondaryButton.IsVisible = false;
        _uninstallButton.IsVisible = false;
        _host.Root.SetFocus(_primaryButton);
        _host.Root.InvalidateLayout();
    }

    internal bool HandleKey(WindowKeyEvent input) =>
        _host.Root.HandleKey(input, this, wrapFocus: true, directionalNavigation: true);

    internal void SetDpi(float dpi) => _host.Root.SetDpi(dpi);

    internal bool Update()
    {
        bool continues = _host.Update();
        if (!continues)
        {
            _host.ResetClock();
        }

        return continues;
    }

    internal void DrawFrame(SizeF pixelSize) => _host.Draw(pixelSize);

    internal bool HandlePointer(in WindowPointerEvent input) => _host.Root.HandlePointer(input);

    protected override bool HitTestCore(PointF position) => false;

    public void Dispose() => _host.Dispose();

    private static (string Title, string Description) GetCopy(AppInstallationRequest request)
    {
        string title;
        string description;
        switch (request.Action)
        {
            case AppInstallationAction.Install:
                title = $"Install Dameview {request.CurrentVersion}";
                description = "Install Dameview for this account.";
                break;

            case AppInstallationAction.Update:
                title = $"Update Dameview to {request.CurrentVersion}";
                description = $"Replace the installed version {request.InstalledVersion} with this version.";
                break;

            case AppInstallationAction.Reinstall:
                title = $"Reinstall Dameview {request.CurrentVersion}";
                description = "This version of Dameview is already installed.";
                break;

            case AppInstallationAction.Uninstall:
                title = "Uninstall Dameview";
                description = "Remove Dameview from this account. Your settings will be kept.";
                break;

            default:
                throw new InvalidOperationException("Unknown installation action.");
        }

        return (title, description);
    }

    private static string GetPrimaryLabel(AppInstallationAction action) => action switch
    {
        AppInstallationAction.Install => "Install",
        AppInstallationAction.Update => "Update",
        AppInstallationAction.Reinstall => "Reinstall",
        AppInstallationAction.Uninstall => "Uninstall",
        _ => throw new InvalidOperationException("Unknown installation action."),
    };
}
