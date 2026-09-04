using Dameview.Imaging;
using Dameview.Navigation;
using SharpGen.Runtime;

namespace Dameview.Viewing;

// UI-thread owned. The loader delivers only the latest request on this thread.
// Dependencies are borrowed; the application owns their lifetime.
internal sealed class ViewerSession
{
    private readonly FolderNavigator _folderNavigator;
    private readonly IImageLoader _imageLoader;

    internal ViewerSession(
        FolderNavigator folderNavigator,
        IImageLoader imageLoader,
        int width,
        int height)
    {
        _folderNavigator = folderNavigator;
        _imageLoader = imageLoader;
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
        string loadPath = path;
        string? navigationError = null;

        try
        {
            loadPath = Path.GetFullPath(path);
            _folderNavigator.SetCurrent(loadPath);
        }
        catch (Exception exception) when (IsRecoverableImageError(exception))
        {
            navigationError = exception.Message;
        }

        BeginImageLoad(loadPath, navigationError, navigationDirection: 0);
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
            BeginImageLoad(path, navigationError: null, direction);
        }
    }

    private void BeginImageLoad(
        string path,
        string? navigationError,
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
            result => CompleteImageLoad(result, navigationError, navigationDirection));
    }

    private void CompleteImageLoad(
        ImageLoadResult result,
        string? navigationError,
        int navigationDirection)
    {
        switch (result)
        {
            case ImageLoaded loaded:
                Animator.Reset();
                Viewport.SetImageSize(loaded.Image.Width, loaded.Image.Height);
                State = new ViewerSessionState(loaded.Path, loaded, false, null, false);
                if (navigationError is not null)
                {
                    State = State with
                    {
                        Message = $"Image opened, but its folder could not be read: {navigationError}",
                        IsError = true,
                    };
                }
                else
                {
                    string? previousPath = _folderNavigator.GetPreviousPath();
                    string? nextPath = _folderNavigator.GetNextPath();
                    _imageLoader.Preload(
                        navigationDirection < 0
                            ? [previousPath, nextPath]
                            : [nextPath, previousPath]);
                }

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
    bool IsError);
