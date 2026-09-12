using Dameview.Imaging;
using Dameview.Navigation;

namespace Dameview.Viewing;

// UI-thread owned. The loader delivers only the latest request on this thread.
// The session owns its monitor, loader, and the representation attached to its
// currently displayed image.
internal sealed class ViewerSession : IDisposable
{
    private readonly FolderNavigator _folderNavigator;
    private readonly IFolderMonitor _folderMonitor;
    private readonly IImageLoader _imageLoader;
    private bool _disposed;

    internal ViewerSession(
        FolderNavigator folderNavigator,
        IFolderMonitor folderMonitor,
        IImageLoader imageLoader)
    {
        _folderNavigator = folderNavigator;
        _imageLoader = imageLoader;
        _folderMonitor = folderMonitor;
        _folderMonitor.Updated += HandleFolderUpdated;
        Viewport = new ImageViewport(0, 0);
        Animator = new ViewportAnimator(Viewport);
    }

    internal event Action? StateChanged;
    internal ViewerSessionState State { get; private set; } = new(null, null, false, null, false, []);
    internal ImageViewport Viewport { get; }
    internal ViewportAnimator Animator { get; }

    internal void SetSort(FolderSort sort)
    {
        _folderNavigator.SetSort(sort);
        State = State with { FolderEntries = _folderNavigator.GetFiles() };
        if (!State.IsLoading)
        {
            ApplyNavigationResult(navigationDirection: 0);
        }

        StateChanged?.Invoke();
    }

    internal void ShowPreviousImage()
    {
        OpenNavigatedImage(_folderNavigator.MoveToPreviousPath(), direction: -1);
    }

    internal void ShowNextImage()
    {
        OpenNavigatedImage(_folderNavigator.MoveToNextPath(), direction: 1);
    }

    internal void OpenImage(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _imageLoader.Preload([]);

        string fullPath;
        string directoryPath;
        try
        {
            fullPath = Path.GetFullPath(path);
            directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new ArgumentException("The image path has no containing directory.", nameof(path));
        }
        catch (Exception exception) when (IsRecoverablePathError(exception))
        {
            _folderNavigator.Clear();
            _folderMonitor.Close();
            State = State with
            {
                RequestedPath = path,
                IsLoading = false,
                Message = $"Could not open image: {exception.Message}",
                IsError = true,
                FolderEntries = [],
                FolderError = null,
            };
            StateChanged?.Invoke();
            return;
        }

        if (string.Equals(directoryPath, _folderMonitor.CurrentDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _folderNavigator.SetCurrent(fullPath);
            BeginImageLoad(fullPath, navigationDirection: 0);
            return;
        }

        _folderNavigator.Clear();
        State = State with
        {
            FolderEntries = [],
            FolderError = null,
        };
        BeginImageLoad(fullPath, navigationDirection: 0);
        _folderMonitor.Open(directoryPath);
    }

    internal void SelectImage(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.Equals(path, State.RequestedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _folderNavigator.SetCurrent(path);
        BeginImageLoad(path, navigationDirection: 0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _folderMonitor.Updated -= HandleFolderUpdated;
        State.DisplayedImage?.Dispose();
        _imageLoader.Dispose();
        _folderMonitor.Dispose();
    }

    private void HandleFolderUpdated(FolderUpdate update)
    {
        if (_disposed)
        {
            return;
        }

        if (update.Error is null)
        {
            _folderNavigator.SetFiles(update.Entries, State.RequestedPath!);
        }

        State = update.Error is null
            ? State with { FolderEntries = _folderNavigator.GetFiles(), FolderError = null }
            : State with { FolderEntries = [], FolderError = update.Error };

        if (!State.IsLoading)
        {
            ApplyNavigationResult(navigationDirection: 0);
        }

        StateChanged?.Invoke();
    }

    private void ApplyNavigationResult(int navigationDirection)
    {
        if (!State.IsError && State.FolderError is null)
        {
            string? previousPath = _folderNavigator.GetPreviousPath();
            string? nextPath = _folderNavigator.GetNextPath();
            _imageLoader.Preload(navigationDirection < 0
                ? [previousPath, nextPath]
                : [nextPath, previousPath]);
        }
    }

    private static bool IsRecoverablePathError(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or OverflowException;
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
            (result as ImageLoaded)?.Dispose();
            return;
        }

        ImageLoaded? previousImage = null;
        try
        {
            switch (result)
            {
                case ImageLoaded { IsPreview: true } preview:
                    previousImage = State.DisplayedImage;
                    Animator.Reset();
                    Viewport.SetImageSize(
                        preview.Representation.Width,
                        preview.Representation.Height);
                    State = State with { DisplayedImage = preview };
                    break;

                case ImageLoaded loaded:
                    previousImage = State.DisplayedImage;
                    Animator.Reset();
                    Viewport.SetImageSize(
                        loaded.Representation.Width,
                        loaded.Representation.Height);
                    State = State with
                    {
                        RequestedPath = loaded.Path,
                        DisplayedImage = loaded,
                        IsLoading = false,
                        Message = null,
                        IsError = false,
                    };
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
        finally
        {
            if (!ReferenceEquals(previousImage, State.DisplayedImage))
            {
                previousImage?.Dispose();
            }
        }
    }
}

// DisplayedImage retains the resources needed to recreate its presentation without
// resetting the viewport. Its representation is owned by the session and must not
// be disposed by consumers.
internal sealed record ViewerSessionState(
    string? RequestedPath,
    ImageLoaded? DisplayedImage,
    bool IsLoading,
    string? Message,
    bool IsError,
    FolderEntry[] FolderEntries,
    string? FolderError = null);
