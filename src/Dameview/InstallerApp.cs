using Dameview.Imaging.Decoding;
using Dameview.Installation;
using Dameview.Rendering;
using Dameview.UI;
using Dameview.UI.Foundation;
using Dameview.Win32;
using Dameview.Win32.Input;
using Vortice.Direct3D;

namespace Dameview;

internal sealed class InstallerApp : IDisposable
{
    private readonly AppInstallationRequest _request;
    private readonly AppWindow _window;
    private readonly D2DRenderer _renderer;
    private readonly InstallerUi _ui;
    private bool _runPortable;
    private bool _confirmingUninstall;
    private bool _uninstallComplete;

    internal InstallerApp(AppInstallationRequest request)
    {
        _request = request;
        string title = request.Action == AppInstallationAction.Uninstall
            ? "Uninstall Dameview"
            : "Install Dameview";
        _window = new AppWindow(title, 580, 435);
        _window.CenterOnPrimaryMonitor();
        _window.SetTitleBarTheme(dark: true, UiTheme.Default.WindowCaptionColor, UiTheme.Default.WindowTextColor);
        _renderer = new D2DRenderer(
            _window.Handle,
            _window.ClientWidth,
            _window.ClientHeight,
            _window.Dpi,
            D2DRenderer.CreateDevice(DriverType.Hardware));
        _ui = new InstallerUi(
            _renderer.DeviceContext,
            _renderer.DirectWriteFactory,
            request,
            _window.Dpi,
            HandlePrimaryAction,
            HandleSecondaryAction,
            BeginUninstall);
        _ui.Invalidated += _window.RequestRepaint;
        _ui.CursorChanged += _window.ApplyCursor;
        _window.RenderFrame += HandleRenderFrame;
        _window.Resized += HandleResize;
        _window.DpiChanged += HandleDpiChanged;
        _window.KeyPressed += HandleKeyPress;
        _window.PointerInput += HandlePointerInput;
    }

    internal bool Run()
    {
        _window.Closed += NativeMethods.RequestMessageLoopExit;
        try
        {
            _window.Run(_renderer.FrameLatencyWaitHandle);
        }
        finally
        {
            _window.Closed -= NativeMethods.RequestMessageLoopExit;
        }

        if (_uninstallComplete)
        {
            AppInstallation.DeleteInstalledFilesAfterExit();
        }

        return _runPortable;
    }

    public void Dispose()
    {
        _ui.Invalidated -= _window.RequestRepaint;
        _ui.CursorChanged -= _window.ApplyCursor;
        _window.RenderFrame -= HandleRenderFrame;
        _window.Resized -= HandleResize;
        _window.DpiChanged -= HandleDpiChanged;
        _window.KeyPressed -= HandleKeyPress;
        _window.PointerInput -= HandlePointerInput;
        _ui.Dispose();
        _renderer.Dispose();
        _window.Dispose();
    }

    private void HandlePrimaryAction()
    {
        if (_uninstallComplete)
        {
            Close();
            return;
        }

        try
        {
            if (_request.Action == AppInstallationAction.Uninstall || _confirmingUninstall)
            {
                AppInstallation.Uninstall();
                _uninstallComplete = true;
                _ui.ShowUninstallComplete();
                return;
            }

            using var imageDecoder = new ImageDecoder();
            string sourcePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Could not determine the executable path.");
            AppInstallation.Install(sourcePath, imageDecoder.GetProbablySupportedExtensions());
            AppInstallation.LaunchInstalled();
            Close();
        }
        catch (Exception exception)
        {
            _ui.ShowError(exception.Message);
        }
    }

    private void HandleSecondaryAction()
    {
        if (_confirmingUninstall)
        {
            _confirmingUninstall = false;
            _ui.ShowInstallationActions();
            return;
        }

        if (_request.Action == AppInstallationAction.Uninstall)
        {
            Close();
            return;
        }

        RunPortable();
    }

    private void BeginUninstall()
    {
        _confirmingUninstall = true;
        _ui.ShowUninstallConfirmation();
    }

    private void RunPortable()
    {
        _runPortable = true;
        Close();
    }

    private void Close() => _window.Close();

    private void HandleKeyPress(WindowKeyEvent input)
    {
        if (input.Key == WindowKey.Escape)
        {
            if (_uninstallComplete)
            {
                Close();
            }
            else if (_confirmingUninstall)
            {
                _confirmingUninstall = false;
                _ui.ShowInstallationActions();
            }
            else
            {
                Close();
            }

            return;
        }

        if (_ui.HandleKey(input))
        {
            _window.RequestRepaint();
        }
    }

    private void HandleDpiChanged(float dpi)
    {
        _renderer.SetDpi(dpi);
        _ui.SetDpi(dpi);
    }

    private void HandleResize(int width, int height)
    {
        _renderer.Resize(width, height);
        _window.RequestRepaint();
    }

    private void HandlePointerInput(WindowPointerEvent input)
    {
        _ui.HandlePointer(input);
    }

    private void HandleRenderFrame()
    {
        bool animationContinues = _ui.Update();
        _renderer.Render(_ui.DrawFrame, _ui.Palette.Background);
        if (animationContinues)
        {
            _window.RequestRepaint();
        }
    }
}
