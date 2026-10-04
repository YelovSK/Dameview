using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Dameview.Diagnostics;
using Dameview.Imaging.Loading;
using Dameview.Notifications;
using Dameview.Win32;

namespace Dameview.Commands;

/// <summary>What can be done with an image file through the clipboard and the shell.</summary>
internal sealed class FileActions : IDisposable
{
    private readonly nint _window;
    private readonly ImageLoadService _imageLoadService;
    private readonly ToastService _toasts;
    private readonly IReadOnlySet<string> _decodableExtensions;
    private CancellationTokenSource? _copyImageCancellation;

    internal FileActions(
        nint window,
        ImageLoadService imageLoadService,
        ToastService toasts,
        IReadOnlySet<string> decodableExtensions)
    {
        _window = window;
        _imageLoadService = imageLoadService;
        _toasts = toasts;
        _decodableExtensions = decodableExtensions;
    }

    internal string? PickImage()
    {
        try
        {
            return FilePicker.PickFile(_window, "Images", _decodableExtensions);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            Log.Error("Window", "The file picker could not be opened.", exception);
            _toasts.Notify("Could not open the file picker.", ToastSeverity.Error);
            return null;
        }
    }

    internal void CopyPath(string path)
    {
        if (Win32Clipboard.TrySetText(path))
        {
            _toasts.Notify($"Copied the path of {Path.GetFileName(path)}.", ToastSeverity.Success);
        }
        else
        {
            _toasts.Notify("Could not copy the path to the clipboard.", ToastSeverity.Error);
        }
    }

    internal void CopyFile(string path)
    {
        if (Win32Clipboard.TrySetFile(path))
        {
            _toasts.Notify($"Copied {Path.GetFileName(path)}.", ToastSeverity.Success);
        }
        else
        {
            _toasts.Notify("Could not copy the file to the clipboard.", ToastSeverity.Error);
        }
    }

    [SuppressMessage("Performance", "CA1822", Justification = "Part of the instance API commands use through the host.")]
    internal void ShowInFolder(string path) => ShellIntegration.ShowInFolder(path);

    internal void OpenWith(string path)
    {
        if (!ShellIntegration.TryShowOpenWith(_window, path))
        {
            _toasts.Notify("Could not show the Open with dialog.", ToastSeverity.Error);
        }
    }

    internal void ShowProperties(string path)
    {
        if (!ShellIntegration.TryShowProperties(_window, path))
        {
            _toasts.Notify("Could not show the file properties.", ToastSeverity.Error);
        }
    }

    internal void MoveToRecycleBin(string path)
    {
        if (ShellIntegration.TryMoveToRecycleBin(_window, path))
        {
            _toasts.Notify($"Moved {Path.GetFileName(path)} to the Recycle Bin.", ToastSeverity.Success);
        }
    }

    internal void CopyImage(string path)
    {
        _copyImageCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _copyImageCancellation = cancellation;
        _ = CopyImageAsync(path, cancellation);
    }

    // Owns the cancellation source, so a newer copy only cancels it and never disposes it
    // while this one still uses its token.
    private async Task CopyImageAsync(string path, CancellationTokenSource cancellation)
    {
        string name = Path.GetFileName(path);
        CancellationToken token = cancellation.Token;
        try
        {
            ClipboardBitmap bitmap;
            try
            {
                bitmap = await _imageLoadService.DecodeClipboardBitmapAsync(path, token);
            }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                Log.Error("Clipboard", $"Could not decode '{name}' for clipboard copy.", error);
                _toasts.Notify($"Could not read {name} to copy it.", ToastSeverity.Error);
                return;
            }

            using (bitmap)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (bitmap.TrySet())
                {
                    _toasts.Notify($"Copied {name} to the clipboard.", ToastSeverity.Success);
                }
                else
                {
                    Log.Warning("Clipboard", $"Could not copy '{name}' to the clipboard.");
                    _toasts.Notify($"Could not copy {name} to the clipboard.", ToastSeverity.Error);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_copyImageCancellation, cancellation))
            {
                _copyImageCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    public void Dispose() => _copyImageCancellation?.Cancel();
}
