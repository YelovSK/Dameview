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
    private CancellationTokenSource? _copyImageCancellation;

    internal FileActions(nint window, ImageLoadService imageLoadService, ToastService toasts)
    {
        _window = window;
        _imageLoadService = imageLoadService;
        _toasts = toasts;
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
        _copyImageCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _copyImageCancellation = cancellation;

        _imageLoadService.DecodeTemporary(path, (image, error) =>
        {
            try
            {
                if (cancellation.IsCancellationRequested)
                {
                    return;
                }

                if (error is not null)
                {
                    Log.Error(
                        "Clipboard",
                        $"Could not decode '{Path.GetFileName(path)}' for clipboard copy.",
                        error);
                    _toasts.Notify(
                        $"Could not read {Path.GetFileName(path)} to copy it.",
                        ToastSeverity.Error);
                    return;
                }

                if (image is null)
                {
                    return;
                }

                if (!Win32Clipboard.TrySetImage(
                        _window,
                        image.Width,
                        image.Height,
                        image.Stride,
                        image.Span))
                {
                    Log.Warning("Clipboard", $"Could not copy '{Path.GetFileName(path)}' to the clipboard.");
                    _toasts.Notify(
                        $"Could not copy {Path.GetFileName(path)} to the clipboard.",
                        ToastSeverity.Error);
                    return;
                }

                _toasts.Notify(
                    $"Copied {Path.GetFileName(path)} to the clipboard.",
                    ToastSeverity.Success);
            }
            finally
            {
                image?.Dispose();
                if (ReferenceEquals(_copyImageCancellation, cancellation))
                {
                    _copyImageCancellation = null;
                    cancellation.Dispose();
                }
            }
        }, cancellation.Token);
    }

    public void Dispose()
    {
        _copyImageCancellation?.Cancel();
        _copyImageCancellation?.Dispose();
    }
}
