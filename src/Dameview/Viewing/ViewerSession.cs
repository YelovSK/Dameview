using Dameview.Imaging;
using Dameview.Navigation;
using SharpGen.Runtime;

namespace Dameview.Viewing;

// UI-thread owned. The loader delivers only the latest request on this thread.
// Dependencies are borrowed; the application owns their lifetime.
internal sealed class ViewerSession : IDisposable
{
    private readonly FolderNavigator _folderNavigator;
    private readonly IImageLoader _imageLoader;
    private readonly IFolderScanner _folderScanner;
    private readonly Action<Action> _postToUi;
    private CancellationTokenSource? _folderScan;
    private string? _navigationError;
    private bool _disposed;

    internal ViewerSession(
        FolderNavigator folderNavigator,
        IImageLoader imageLoader,
        IFolderScanner folderScanner,
        Action<Action> postToUi,
        int width,
        int height)
    {
        _folderNavigator = folderNavigator;
        _imageLoader = imageLoader;
        _folderScanner = folderScanner;
        _postToUi = postToUi;
        Viewport = new ImageViewport(width, height);
        Animator = new ViewportAnimator(Viewport);
    }

    internal event Action? StateChanged;
    internal ViewerSessionState State { get; private set; } = new(null, null, false, null, false);
    internal ImageViewport Viewport { get; }
    internal ViewportAnimator Animator { get; }

    internal void ShowPreviousImage()
    {
        OpenNavigatedImage(_folderNavigator.MoveToPreviousPath(), direction: -1);
    }

    internal void ShowNextImage()
    {
        OpenNavigatedImage(_folderNavigator.MoveToNextPath(), direction: 1);
    }

    internal void SetViewportSize(int width, int height)
    {
        Animator.Reset();
        Viewport.SetViewportSize(width, height);
    }

    internal void OpenImage(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _folderScan?.Cancel();
        _folderScan?.Dispose();
        _folderScan = new CancellationTokenSource();
        _folderNavigator.Clear();
        _imageLoader.Preload([]);
        _navigationError = null;
        string loadPath = path;
        string? directoryPath = null;
        try
        {
            loadPath = Path.GetFullPath(path);
            directoryPath = Path.GetDirectoryName(loadPath)
                ?? throw new ArgumentException("The image path has no containing directory.", nameof(path));
        }
        catch (Exception exception) when (IsRecoverableImageError(exception))
        {
            _navigationError = exception.Message;
        }

        State = State with { IsScanningFolder = directoryPath is not null };
        BeginImageLoad(loadPath, navigationDirection: 0);
        if (directoryPath is not null)
        {
            _ = ScanFolderAsync(directoryPath, loadPath, _folderScan.Token);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _folderScan?.Cancel();
        _folderScan?.Dispose();
        _folderScan = null;
    }

    private async Task ScanFolderAsync(string directoryPath, string currentPath, CancellationToken token)
    {
        FolderEntry[] files = [];
        string? error = null;
        try
        {
            files = await _folderScanner.ScanAsync(directoryPath, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (IsRecoverableImageError(exception))
        {
            error = exception.Message;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        _postToUi(() =>
        {
            if (_disposed || token.IsCancellationRequested)
            {
                return;
            }

            _navigationError = error;
            if (error is null)
            {
                _folderNavigator.SetFiles(files, currentPath);
            }

            State = State with { IsScanningFolder = false };
            if (!State.IsLoading && !State.IsError)
            {
                ApplyNavigationResult(navigationDirection: 0);
            }

            StateChanged?.Invoke();
        });
    }

    private void ApplyNavigationResult(int navigationDirection)
    {
        if (_navigationError is not null)
        {
            State = State with
            {
                Message = $"Image opened, but its folder could not be read: {_navigationError}",
                IsError = true,
            };
        }
        else if (!State.IsScanningFolder)
        {
            string? previousPath = _folderNavigator.GetPreviousPath();
            string? nextPath = _folderNavigator.GetNextPath();
            _imageLoader.Preload(navigationDirection < 0
                ? [previousPath, nextPath]
                : [nextPath, previousPath]);
        }
    }

    private static bool IsRecoverableImageError(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or OverflowException
            or SharpGenException;
    }

    private void OpenNavigatedImage(string? path, int direction)
    {
        if (path is not null)
        {
            BeginImageLoad(path, direction);
        }
    }

    private void BeginImageLoad(
        string path,
        int navigationDirection)
    {
        State = State with
        {
            RequestedPath = path,
            IsLoading = true,
            Message = $"Loading {Path.GetFileName(path)}…",
            IsError = false,
        };
        StateChanged?.Invoke();
        _imageLoader.Load(
            path,
            result => CompleteImageLoad(result, navigationDirection));
    }

    private void CompleteImageLoad(
        ImageLoadResult result,
        int navigationDirection)
    {
        if (_disposed)
        {
            return;
        }

        switch (result)
        {
            case ImageLoaded loaded:
                Animator.Reset();
                Viewport.SetImageSize(loaded.Image.Width, loaded.Image.Height);
                State = new ViewerSessionState(loaded.Path, loaded, false, null, false, State.IsScanningFolder);
                ApplyNavigationResult(navigationDirection);

                break;

            case ImageLoadFailed failed:
                State = State with
                {
                    IsLoading = false,
                    Message = $"Could not open {Path.GetFileName(failed.Path)}: {failed.Exception.Message}",
                    IsError = true,
                };
                break;
        }

        StateChanged?.Invoke();
    }
}

// DisplayedImage retains CPU pixels so graphics resources can be recreated
// without reloading the file or resetting the viewport.
internal sealed record ViewerSessionState(
    string? RequestedPath,
    ImageLoaded? DisplayedImage,
    bool IsLoading,
    string? Message,
    bool IsError,
    bool IsScanningFolder = false);
