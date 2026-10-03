using System.Diagnostics.CodeAnalysis;
using Dameview.Imaging;
using Dameview.Imaging.Loading;
using Dameview.Rendering;
using Vortice.Direct2D1;

namespace Dameview.UI.Presentation;

// UI-thread owned. The cache owns every bitmap; displayed images hold leases
// that protect their entries from eviction.
internal sealed class RenderBitmapCache : IDisposable
{
    private readonly Dictionary<string, LinkedListNode<CachedBitmap>> _entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<CachedBitmap> _recentlyUsed = new();
    private readonly Action<ID2D1Bitmap1> _disposeBitmap;
    private readonly long _capacityBytes;
    private long _sizeBytes;
    private bool _disposed;

    internal RenderBitmapCache(long capacityBytes)
        : this(capacityBytes, static bitmap => bitmap.Dispose())
    {
    }

    internal RenderBitmapCache(
        long capacityBytes,
        Action<ID2D1Bitmap1> disposeBitmap)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityBytes);
        ArgumentNullException.ThrowIfNull(disposeBitmap);
        _capacityBytes = capacityBytes;
        _disposeBitmap = disposeBitmap;
    }

    /// <summary>How many bytes speculative images can still take without evicting anything.</summary>
    internal long FreeBytes => Math.Max(0, _capacityBytes - _sizeBytes);

    internal bool Contains(string path) => _entries.ContainsKey(path);

    internal bool TryAcquire(
        string path,
        [NotNullWhen(true)] out CachedBitmapLease? lease)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_entries.TryGetValue(path, out LinkedListNode<CachedBitmap>? node))
        {
            lease = null;
            return false;
        }

        Touch(node);
        lease = Acquire(node.Value);
        return true;
    }

    /// <summary>
    /// Leases the cached bitmap for a path, creating it first if the cache has none. The caller
    /// trims once it has let go of whatever the new bitmap replaces, so that the replaced one
    /// is what gets evicted.
    /// </summary>
    internal CachedBitmapLease GetOrAdd(
        string path,
        int width,
        int height,
        ImageOrientation orientation,
        Func<ID2D1Bitmap1> create)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_entries.TryGetValue(path, out LinkedListNode<CachedBitmap>? node))
        {
            Touch(node);
            return Acquire(node.Value);
        }

        return Acquire(Add(path, width, height, orientation, create));
    }

    /// <summary>Caches a speculative image, unless it is already cached or does not fit.</summary>
    /// <remarks>
    /// A preload may use free space but must never evict, or a folder whose images do not all
    /// fit would throw one out to make room for the next and fetch it again a moment later.
    /// </remarks>
    internal void TryPreload(
        string path,
        int width,
        int height,
        ImageOrientation orientation,
        Func<ID2D1Bitmap1> create)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_entries.ContainsKey(path) && DecodedImage.GetByteCount(width, height) <= FreeBytes)
        {
            Add(path, width, height, orientation, create);
        }
    }

    /// <summary>Also disposes leased bitmaps, so whatever displays them has to be dropped too.</summary>
    internal void Clear()
    {
        foreach (CachedBitmap entry in _recentlyUsed)
        {
            // Read back but never uploaded, so its bitmap was released already.
            if (entry.Pixels is null)
            {
                _disposeBitmap(entry.Bitmap);
            }

            entry.Pixels = null;
        }

        _entries.Clear();
        _recentlyUsed.Clear();
        _sizeBytes = 0;
    }

    /// <summary>
    /// Moves the leased bitmaps to system memory before a device switch, and drops the rest.
    /// Leases stay valid through the following <see cref="Upload"/>.
    /// </summary>
    internal void ReadBack(ID2D1DeviceContext deviceContext)
    {
        // Unleased entries were only preloaded; reloading them later is cheaper than copying
        // them twice on the window thread now.
        LinkedListNode<CachedBitmap>? node = _recentlyUsed.First;
        while (node is not null)
        {
            LinkedListNode<CachedBitmap>? next = node.Next;
            if (node.Value.PinCount == 0)
            {
                Remove(node);
            }

            node = next;
        }

        try
        {
            // Read everything before releasing anything, so a failure leaves the cache usable.
            foreach (CachedBitmap entry in _recentlyUsed)
            {
                entry.Pixels = D2DBitmapFactory.ReadBack(deviceContext, entry.Bitmap);
            }
        }
        catch
        {
            foreach (CachedBitmap entry in _recentlyUsed)
            {
                entry.Pixels = null;
            }

            throw;
        }

        foreach (CachedBitmap entry in _recentlyUsed)
        {
            _disposeBitmap(entry.Bitmap);
        }
    }

    /// <summary>
    /// Puts the read-back bitmaps on the replacement device. Nothing changes if this fails, so
    /// <see cref="Clear"/> still knows which bitmaps are already gone.
    /// </summary>
    internal void Upload(ID2D1DeviceContext deviceContext)
    {
        var uploaded = new List<ID2D1Bitmap1>(_recentlyUsed.Count);
        try
        {
            foreach (CachedBitmap entry in _recentlyUsed)
            {
                uploaded.Add(D2DBitmapFactory.Create(deviceContext, entry.Pixels!));
            }
        }
        catch
        {
            uploaded.ForEach(_disposeBitmap);
            throw;
        }

        int index = 0;
        foreach (CachedBitmap entry in _recentlyUsed)
        {
            entry.Bitmap = uploaded[index++];
            entry.Pixels = null;
        }
    }

    internal void Trim()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (_sizeBytes > _capacityBytes)
        {
            LinkedListNode<CachedBitmap>? candidate = _recentlyUsed.Last;
            while (candidate is not null && candidate.Value.PinCount > 0)
            {
                candidate = candidate.Previous;
            }

            if (candidate is null)
            {
                return;
            }

            Remove(candidate);
        }
    }

    private CachedBitmap Add(
        string path,
        int width,
        int height,
        ImageOrientation orientation,
        Func<ID2D1Bitmap1> create)
    {
        long sizeBytes = DecodedImage.GetByteCount(width, height);
        var entry = new CachedBitmap(path, create(), width, height, orientation, sizeBytes);
        LinkedListNode<CachedBitmap> node = _recentlyUsed.AddFirst(entry);
        _entries.Add(path, node);
        _sizeBytes += sizeBytes;
        return entry;
    }

    private CachedBitmapLease Acquire(CachedBitmap bitmap)
    {
        bitmap.PinCount++;
        return new CachedBitmapLease(this, bitmap);
    }

    internal void Release(CachedBitmap bitmap)
    {
        if (_disposed)
        {
            return;
        }

        if (bitmap.PinCount <= 0)
        {
            throw new InvalidOperationException("The cached bitmap has no active lease.");
        }

        bitmap.PinCount--;
        Trim();
    }

    private void Touch(LinkedListNode<CachedBitmap> node)
    {
        _recentlyUsed.Remove(node);
        _recentlyUsed.AddFirst(node);
    }

    private void Remove(LinkedListNode<CachedBitmap> node)
    {
        _recentlyUsed.Remove(node);
        _entries.Remove(node.Value.Path);
        _sizeBytes -= node.Value.SizeBytes;
        _disposeBitmap(node.Value.Bitmap);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Clear();
    }
}

internal sealed class CachedBitmap(
    string path,
    ID2D1Bitmap1 bitmap,
    int width,
    int height,
    ImageOrientation orientation,
    long sizeBytes)
{
    internal string Path { get; } = path;
    // Replaced when the entry moves to another device, which is why leases read through here.
    internal ID2D1Bitmap1 Bitmap { get; set; } = bitmap;

    /// <summary>
    /// Held only between reading the bitmap back and uploading it again; while it is set,
    /// <see cref="Bitmap"/> has been released.
    /// </summary>
    internal DecodedImage? Pixels { get; set; }
    internal int Width { get; } = width;
    internal int Height { get; } = height;
    internal ImageOrientation Orientation { get; } = orientation;
    internal long SizeBytes { get; } = sizeBytes;
    internal int PinCount { get; set; }
}

/// <summary>Keeps a cached bitmap from being evicted until disposed.</summary>
internal sealed class CachedBitmapLease(RenderBitmapCache owner, CachedBitmap entry)
    : ImageRepresentation(entry.Width, entry.Height, entry.Orientation)
{
    internal CachedBitmap Entry { get; } = entry;
    internal ID2D1Bitmap1 Bitmap => Entry.Bitmap;

    protected override void DisposeCore() => owner.Release(Entry);
}
