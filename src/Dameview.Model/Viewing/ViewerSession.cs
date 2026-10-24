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
    private readonly FolderSortMemory _folderSorts;
    private readonly IFolderMonitor _folderMonitor;
    private readonly IImageLoader _imageLoader;
    private long _scanStarted;
    private int _preloadAhead = 1;
    private BrowseDirection _browseDirection = BrowseDirection.Forward;
    private FolderSort _defaultSort = FolderSort.NameAscending;
    private bool _disposed;

    internal ViewerSession(
        FolderNavigator folderNavigator,
        FolderSortMemory folderSorts,
        IFolderMonitor folderMonitor,
        IImageLoader imageLoader)
    {
        _folderNavigator = folderNavigator;
        _folderSorts = folderSorts;
        _imageLoader = imageLoader;
        _folderMonitor = folderMonitor;
        _folderMonitor.Updated += HandleFolderUpdated;
        _folderSorts.Changed += HandleFolderSortsChanged;
        Viewport = new ImageViewport(Size.Empty);
        Animator = new ViewportAnimator(Viewport);
    }

    internal event Action? StateChanged;
    internal ViewerSessionState State { get; private set; } = new(null, null, false, null, false, []);
    internal ImageViewport Viewport { get; }
    internal ViewportAnimator Animator { get; }

    /// <summary>Whether next and previous lead to another image, which the name filter may rule out.</summary>
    internal bool HasOtherImages => _folderNavigator.HasOtherMatch;

    /// <summary>The sort used for folders that neither have nor inherit a sort of their own.</summary>
    internal void SetDefaultSort(FolderSort sort)
    {
        _defaultSort = sort;
        _folderNavigator.SetSort(GetEffectiveSort());
        RefreshFolderEntries();
    }

    /// <summary>
    /// Remembers a sort for the open folder. While subfolders are shown, it applies to them too.
    /// </summary>
    /// <param name="sort">Null forgets the folder's own sort, so it inherits one again.</param>
    internal void SetFolderSort(FolderSort? sort)
    {
        if (_folderMonitor.Scope is { } scope)
        {
            _folderSorts.Set(scope.DirectoryPath, sort, State.FlattensFolder);
        }
    }

    private FolderSort GetEffectiveSort() => State.SortChoice.Sort ?? _defaultSort;

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

    private void HandleFolderSortsChanged()
    {
        // Most changes are to folders this tab is not in, which then cost no refresh.
        if (_folderMonitor.Scope is { } scope && ResolveSort(scope.DirectoryPath))
        {
            RefreshFolderEntries();
        }
    }

    /// <returns>Whether the folder's sort changed.</returns>
    private bool ResolveSort(string folderPath)
    {
        FolderSortChoice choice = _folderSorts.Resolve(folderPath);
        if (choice == State.SortChoice)
        {
            return false;
        }

        State = State with { SortChoice = choice };
        _folderNavigator.SetSort(GetEffectiveSort());
        return true;
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
            PreloadNeighbours();
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
            PreloadNeighbours();
        }
    }

    internal void ShowPreviousImage()
    {
        _browseDirection = BrowseDirection.Backward;
        OpenNavigatedImage(_folderNavigator.MoveToPreviousPath());
    }

    internal void ShowNextImage()
    {
        _browseDirection = BrowseDirection.Forward;
        OpenNavigatedImage(_folderNavigator.MoveToNextPath());
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
            MoveTo(fullPath);
            BeginImageLoad(fullPath);
            return;
        }

        StoreNameFilter(string.Empty);
        OpenFolder(new FolderScope(directoryPath, State.FlattensFolder), fullPath);
        BeginImageLoad(fullPath);
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
            BeginImageLoad(path);
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

        MoveTo(path);
        BeginImageLoad(path);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _folderMonitor.Updated -= HandleFolderUpdated;
        _folderSorts.Changed -= HandleFolderSortsChanged;
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
                    OpenNavigatedImage(_folderNavigator.MoveToNextPath());
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
            BeginImageLoad(first);
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
            PreloadNeighbours();
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
        ResolveSort(scope.DirectoryPath);
        _folderNavigator.SetCurrent(imagePath);
        _browseDirection = BrowseDirection.Forward;
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

    private void PreloadNeighbours()
    {
        if (!State.IsError && State.FolderError is null)
        {
            // The nearest image on each side comes first, since even a change of direction needs one.
            int step = _browseDirection == BrowseDirection.Backward ? -1 : 1;
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

    private void OpenNavigatedImage(string? path)
    {
        if (path is not null)
        {
            BeginImageLoad(path);
        }
    }

    private void MoveTo(string path)
    {
        if (_folderNavigator.GetDirectionTo(path) is { } direction)
        {
            _browseDirection = direction;
        }

        _folderNavigator.SetCurrent(path);
    }

    private void BeginImageLoad(string path)
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
        _imageLoader.Load(path, CompleteImageLoad);
    }

    private void ShowInViewport(ImageRepresentation representation)
    {
        Viewport.SetImageSize(
            new Size(representation.Width, representation.Height),
            representation.Orientation);
    }

    private void CompleteImageLoad(ImageLoadResult result)
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
                    PreloadNeighbours();

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
    FolderSortChoice SortChoice = default,
    string NameFilter = "",
    bool IsScanning = false,
    TimeSpan ScanDuration = default);
