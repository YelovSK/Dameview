using System.Diagnostics;
using System.Drawing;
using Dameview.Imaging;
using Dameview.Imaging.Loading;
using Dameview.Navigation;

namespace Dameview.Viewing;

// Owner-thread confined. The monitor and loader deliver their results on this thread.
// The session owns its monitor, loader, and the representation attached to its
// currently displayed image.
internal sealed class ViewerSession : IDisposable
{
    private readonly FolderNavigator _folderNavigator;
    private readonly IFolderMonitor _folderMonitor;
    private readonly IImageLoader _imageLoader;
    private long _scanStarted;
    private int _preloadAhead = 1;
    private FolderSort _defaultSort = FolderSort.NameAscending;
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
        Viewport = new ImageViewport(Size.Empty);
        Animator = new ViewportAnimator(Viewport);
    }

    internal event Action? StateChanged;
    internal ViewerSessionState State { get; private set; } = new(null, null, false, null, false, []);
    internal ImageViewport Viewport { get; }
    internal ViewportAnimator Animator { get; }

    /// <summary>Whether next and previous lead to another image, which the name filter may rule out.</summary>
    internal bool HasOtherImages => _folderNavigator.HasOtherMatch;

    /// <summary>The sort this tab uses while it has no sort of its own.</summary>
    internal void SetDefaultSort(FolderSort sort)
    {
        _defaultSort = sort;
        if (State.SortOverride is null)
        {
            ApplySort();
        }
    }

    /// <param name="sort">This tab's own sort, or null to follow the default.</param>
    internal void SetSortOverride(FolderSort? sort)
    {
        State = State with { SortOverride = sort };
        ApplySort();
    }

    /// <summary>Lists and navigates only the images whose names contain the filter, without leaving the current image.</summary>
    internal void SetNameFilter(string filter)
    {
        if (filter == State.NameFilter)
        {
            return;
        }

        StoreNameFilter(filter);
        RefreshFolderEntries();
    }

    private void ApplySort()
    {
        _folderNavigator.SetSort(State.SortOverride ?? _defaultSort);
        RefreshFolderEntries();
    }

    private void RefreshFolderEntries()
    {
        State = State with
        {
            FolderEntries = _folderNavigator.GetFiles(),
            CurrentEntry = _folderNavigator.CurrentEntry,
        };
        if (!State.IsLoading)
        {
            PreloadNeighbours(navigationDirection: 0);
        }

        StateChanged?.Invoke();
    }

    /// <param name="count">How many images to preload in the direction of browsing. One behind is always preloaded too.</param>
    internal void SetPreloadAhead(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        _preloadAhead = count;
        if (!State.IsLoading)
        {
            PreloadNeighbours(navigationDirection: 0);
        }
    }

    internal void ShowPreviousImage()
    {
        OpenNavigatedImage(_folderNavigator.MoveToPreviousPath(), direction: -1);
    }

    internal void ShowNextImage()
    {
        OpenNavigatedImage(_folderNavigator.MoveToNextPath(), direction: 1);
    }

    /// <summary>Opens an image, or a folder, which then shows the first image its scan finds.</summary>
    internal void OpenImage(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _imageLoader.Preload([]);

        string fullPath;
        string directoryPath;
        bool isFolder;
        try
        {
            fullPath = Path.GetFullPath(path);
            isFolder = Directory.Exists(fullPath);
            directoryPath = isFolder
                ? fullPath
                : Path.GetDirectoryName(fullPath)
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
                CurrentEntry = null,
                FolderError = null,
            };
            StateChanged?.Invoke();
            return;
        }

        // A filter was typed for the folder it narrows, so another folder starts without one.
        if (isFolder)
        {
            StoreNameFilter(string.Empty);
            OpenFolder(new FolderScope(directoryPath, State.FlattensFolder), imagePath: null);
            State = State with
            {
                RequestedPath = null,
                IsLoading = true,
                Message = $"Scanning {fullPath}…",
                IsError = false,
            };
            StateChanged?.Invoke();
            return;
        }

        if (_folderMonitor.Scope?.Contains(fullPath) == true)
        {
            _folderNavigator.SetCurrent(fullPath);
            BeginImageLoad(fullPath, navigationDirection: 0);
            return;
        }

        StoreNameFilter(string.Empty);
        OpenFolder(new FolderScope(directoryPath, State.FlattensFolder), fullPath);
        BeginImageLoad(fullPath, navigationDirection: 0);
    }

    /// <summary>
    /// Switches between the current image's folder alone and that folder with all its subfolders.
    /// Takes effect on the next opened image when no folder is open.
    /// </summary>
    internal void ToggleFlattenFolder()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        State = State with { FlattensFolder = !State.FlattensFolder };
        if (_folderMonitor.Scope is { } scope)
        {
            string? path = State.RequestedPath;
            string directory = path is null ? scope.DirectoryPath : Path.GetDirectoryName(path)!;
            OpenFolder(new FolderScope(directory, State.FlattensFolder), path);
        }

        StateChanged?.Invoke();
    }

    /// <summary>Turns the displayed image, animated, until another image is shown.</summary>
    /// <param name="quarterTurns">Clockwise quarter turns. Negative turns counterclockwise.</param>
    internal void Rotate(int quarterTurns)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State.DisplayedImage is not null && Animator.Rotate(quarterTurns))
        {
            StateChanged?.Invoke();
        }
    }

    /// <summary>Turns or mirrors the displayed image, until another image is shown.</summary>
    internal void ChangeOrientation(Func<ImageOrientation, ImageOrientation> change)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State.DisplayedImage is null)
        {
            return;
        }

        Animator.Reset();
        Viewport.SetOrientation(change(Viewport.Orientation));
        StateChanged?.Invoke();
    }

    /// <summary>For when the graphics device the displayed bitmap lived on was replaced.</summary>
    internal void ReloadDisplayedImage()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ImageLoaded? displayed = State.DisplayedImage;
        State = State with { DisplayedImage = null };
        displayed?.Dispose();
        _imageLoader.Preload([]);
        if (State.RequestedPath is { } path)
        {
            BeginImageLoad(path, navigationDirection: 0);
            return;
        }

        StateChanged?.Invoke();
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
            if (update.Appended)
            {
                _folderNavigator.AddFiles(update.Entries);
            }
            else
            {
                bool wasRemoved = _folderNavigator.CurrentEntry is { } shown && !update.Entries.Any(
                    entry => string.Equals(entry.FullName, shown.FullName, StringComparison.OrdinalIgnoreCase));

                if (wasRemoved)
                {
                    ShowNextImage();
                }

                _folderNavigator.SetFiles(update.Entries, State.RequestedPath);
            }
        }

        State = update.Error is null
            ? State with
            {
                FolderEntries = _folderNavigator.GetFiles(),
                CurrentEntry = _folderNavigator.CurrentEntry,
                FolderError = null,
            }
            : State with { FolderEntries = [], CurrentEntry = null, FolderError = update.Error };

        // Rescans after a change finish too, but only the scan that opened the folder is timed.
        if (update.ScanFinished && State.IsScanning)
        {
            State = State with { IsScanning = false, ScanDuration = Stopwatch.GetElapsedTime(_scanStarted) };
        }

        // A folder opened on its own shows the first image found, or says why there is none.
        if (State.RequestedPath is null && State.FolderEntries.Length > 0)
        {
            string first = State.FolderEntries[0].FullName;
            _folderNavigator.SetCurrent(first);
            BeginImageLoad(first, navigationDirection: 0);
            return;
        }

        if (State.RequestedPath is null && update.ScanFinished)
        {
            State = State with
            {
                IsLoading = false,
                Message = update.Error ?? $"No images found in {_folderMonitor.Scope?.DirectoryPath}.",
                IsError = true,
            };
        }

        if (!State.IsLoading)
        {
            PreloadNeighbours(navigationDirection: 0);
        }

        StateChanged?.Invoke();
    }

    private void StoreNameFilter(string filter)
    {
        _folderNavigator.NameFilter = filter;
        State = State with { NameFilter = filter };
    }

    private void OpenFolder(FolderScope scope, string? imagePath)
    {
        _folderNavigator.Clear();
        _folderNavigator.SetCurrent(imagePath);
        State = State with
        {
            FolderEntries = [],
            CurrentEntry = null,
            FolderError = null,
            IsScanning = true,
        };
        _scanStarted = Stopwatch.GetTimestamp();
        _folderMonitor.Open(scope);
    }

    private void PreloadNeighbours(int navigationDirection)
    {
        if (!State.IsError && State.FolderError is null)
        {
            // The nearest image on each side comes first, since even a change of direction needs one.
            int step = navigationDirection < 0 ? -1 : 1;
            _imageLoader.Preload(_folderNavigator.GetRelativePaths(
                [step, -step, .. Enumerable.Range(2, _preloadAhead - 1).Select(distance => distance * step)]));
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
            CurrentEntry = _folderNavigator.CurrentEntry,
            IsLoading = true,
            Message = $"Loading {Path.GetFileName(path)}…",
            IsError = false,
        };
        StateChanged?.Invoke();
        _imageLoader.Load(
            path,
            result => CompleteImageLoad(result, navigationDirection));
    }

    private void ShowInViewport(ImageRepresentation representation)
    {
        Viewport.SetImageSize(
            new Size(representation.Width, representation.Height),
            representation.Orientation);
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
                    ShowInViewport(preview.Representation);
                    State = State with { DisplayedImage = preview };
                    break;

                case ImageLoaded loaded:
                    previousImage = State.DisplayedImage;
                    Animator.Reset();
                    ShowInViewport(loaded.Representation);
                    State = State with
                    {
                        RequestedPath = loaded.Path,
                        DisplayedImage = loaded,
                        IsLoading = false,
                        Message = null,
                        IsError = false,
                    };
                    PreloadNeighbours(navigationDirection);

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
// be disposed by consumers. ScanDuration belongs to the last scan that opened a folder and
// means nothing while IsScanning.
internal sealed record ViewerSessionState(
    string? RequestedPath,
    ImageLoaded? DisplayedImage,
    bool IsLoading,
    string? Message,
    bool IsError,
    FolderEntry[] FolderEntries,
    FolderEntry? CurrentEntry = null,
    string? FolderError = null,
    bool FlattensFolder = false,
    FolderSort? SortOverride = null,
    string NameFilter = "",
    bool IsScanning = false,
    TimeSpan ScanDuration = default);
