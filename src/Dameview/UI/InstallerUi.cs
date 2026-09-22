using System.Drawing;
using Dameview.Installation;
using Dameview.UI.Animation;
using Dameview.UI.Components;
using Dameview.UI.Foundation;
using Dameview.UI.Layout;
using Dameview.Win32.Input;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Dameview.UI;

internal sealed class InstallerUi : UiElement, IDisposable
{
    private const float PanelWidth = 500.0f;
    private const float PanelHeight = 320.0f;
    private const float PanelPadding = 24.0f;

    private readonly ID2D1DeviceContext _deviceContext;
    private readonly ID2D1SolidColorBrush _brush;
    private readonly UiTextLayoutCache _textLayouts;
    // Counted but never shown: the installer has no performance overlay to read it.
    private readonly UiDrawTally _drawTally = new();
    private readonly AppInstallationRequest _request;
    private readonly TextBlock _title;
    private readonly TextBlock _description;
    private readonly TextBlock _location;
    private readonly TextBlock _status;
    private readonly Button _primaryButton;
    private readonly Button _secondaryButton;
    private readonly Button _uninstallButton;
    private readonly StackPanel _buttons;
    private readonly UiAnimationClock _animationClock = new();
    private readonly UiRoot _root;
    private RectangleF _panelBounds;

    internal InstallerUi(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        AppInstallationRequest request,
        float dpi,
        Action primaryAction,
        Action secondaryAction,
        Action uninstall)
    {
        _deviceContext = deviceContext;
        _brush = deviceContext.CreateSolidColorBrush(default(Color4));
        _textLayouts = new UiTextLayoutCache(directWriteFactory);
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
        };
        _buttons = new StackPanel(
            UiOrientation.Horizontal,
            UiDesign.Spacing,
            StackPanelDistribution.Equal,
            _primaryButton,
            _secondaryButton);

        AddChild(_title);
        AddChild(_description);
        AddChild(_location);
        AddChild(_status);
        AddChild(_buttons);
        AddChild(_uninstallButton);
        _root = new UiRoot(this, dpi, _textLayouts);
        _root.SetFocus(_primaryButton);
    }

    internal event Action? Invalidated
    {
        add => _root.Invalidated += value;
        remove => _root.Invalidated -= value;
    }

    internal event Action<WindowCursor>? CursorChanged
    {
        add => _root.CursorChanged += value;
        remove => _root.CursorChanged -= value;
    }

    internal UiTheme Palette { get; } = UiTheme.Default;

    internal void ShowError(string message)
    {
        _status.Text = message;
        _status.Tone = UiTextTone.Error;
        _status.IsVisible = true;
        _root.InvalidateVisual();
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
        _root.SetFocus(_primaryButton);
        _root.InvalidateLayout();
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
        _root.SetFocus(_primaryButton);
        _root.InvalidateLayout();
    }

    internal void ShowUninstallComplete()
    {
        _description.Text = "Dameview was uninstalled. Your settings were kept.";
        _location.IsVisible = false;
        _status.IsVisible = false;
        _primaryButton.Label = "Close";
        _secondaryButton.IsVisible = false;
        _uninstallButton.IsVisible = false;
        _root.SetFocus(_primaryButton);
        _root.InvalidateLayout();
    }

    internal bool HandleKey(WindowKeyEvent input) =>
        _root.HandleKey(input, this, wrapFocus: true, directionalNavigation: true);

    internal void SetDpi(float dpi) => _root.SetDpi(dpi);

    internal bool Update()
    {
        UiUpdateContext context = _animationClock.GetNextFrame();
        bool continues = _root.Update(context);
        if (!continues)
        {
            _animationClock.Reset();
        }

        return continues;
    }

    internal void DrawFrame(SizeF pixelSize)
    {
        var context = new UiDrawContext(
            _deviceContext, _brush, _textLayouts, _drawTally, Palette, _root.Dpi);
        _root.Draw(context, pixelSize);
    }

    internal bool HandlePointer(in WindowPointerEvent input) => _root.HandlePointer(input);

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float contentWidth = MathF.Max(0.0f, MathF.Min(PanelWidth, availableSize.Width) - 2.0f * PanelPadding);
        _title.Measure(new SizeF(contentWidth, 36.0f));
        _description.Measure(new SizeF(contentWidth, 48.0f));
        _location.Measure(new SizeF(contentWidth, 24.0f));
        _status.Measure(new SizeF(contentWidth, 48.0f));
        _buttons.Measure(new SizeF(contentWidth, 36.0f));
        _uninstallButton.Measure(new SizeF(140.0f, 36.0f));
        return availableSize;
    }

    protected override void ArrangeCore(SizeF finalSize)
    {
        float width = MathF.Min(PanelWidth, MathF.Max(0.0f, finalSize.Width - 2.0f * UiDesign.WindowMargin));
        float height = MathF.Min(PanelHeight, MathF.Max(0.0f, finalSize.Height - 2.0f * UiDesign.WindowMargin));
        _panelBounds = new RectangleF(
            (finalSize.Width - width) / 2.0f,
            (finalSize.Height - height) / 2.0f,
            width,
            height);

        float x = _panelBounds.X + PanelPadding;
        float y = _panelBounds.Y + PanelPadding;
        float contentWidth = MathF.Max(0.0f, width - 2.0f * PanelPadding);
        _title.Arrange(new RectangleF(x, y, contentWidth, 36.0f));
        y += 44.0f;
        _description.Arrange(new RectangleF(x, y, contentWidth, 48.0f));
        y += 56.0f;
        _location.Arrange(new RectangleF(x, y, contentWidth, 24.0f));
        y += 32.0f;
        _status.Arrange(new RectangleF(x, y, contentWidth, 48.0f));
        float footerY = _panelBounds.Bottom - PanelPadding - 36.0f;
        float buttonsY = _uninstallButton.IsVisible ? footerY - 44.0f : footerY;
        _buttons.Arrange(new RectangleF(
            x,
            buttonsY,
            contentWidth,
            36.0f));
        _uninstallButton.Arrange(new RectangleF(
            _panelBounds.X + (_panelBounds.Width - 140.0f) / 2.0f,
            footerY,
            140.0f,
            36.0f));
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        var panel = new RoundedRectangle(
            _panelBounds,
            UiDesign.PanelCornerRadius,
            UiDesign.PanelCornerRadius);
        context.FillRoundedRectangle(panel, context.Palette.Surface);
        context.DrawRoundedRectangle(panel, context.Palette.SurfaceBorder);
    }

    protected override bool HitTestCore(PointF position) => false;

    public void Dispose()
    {
        _root.ClearPointer();
        _root.SetFocus(null);
        _textLayouts.Dispose();
        _brush.Dispose();
    }

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
