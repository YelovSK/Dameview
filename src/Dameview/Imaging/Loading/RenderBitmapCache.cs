using System.Diagnostics.CodeAnalysis;
using Dameview.Rendering;
using Vortice.Direct2D1;

namespace Dameview.Imaging.Loading;

// UI-thread owned, except for reserving space. The cache owns every bitmap; displayed images
// hold leases that protect their entries from eviction.
internal sealed class RenderBitmapCache : IDisposable
{
    private readonly Dictionary<string, LinkedListNode<CachedBitmap>> _entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<CachedBitmap> _recentlyUsed = new();
    private readonly Action<ID2D1Bitmap1> _disposeBitmap;
    private readonly long _capacityBytes;
    private long _sizeBytes;
    private long _reservedBytes;
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

    /// <summary>How many bytes can still be reserved.</summary>
    internal long FreeBytes =>
        Math.Max(0, _capacityBytes - Volatile.Read(ref _sizeBytes) - Volatile.Read(ref _reservedBytes));

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
    /// Takes ownership of a bitmap for a path that isn't cached yet, and leases it. The caller
    /// trims once it has let go of whatever the new bitmap replaces, so that the replaced one
    /// is what gets evicted.
    /// </summary>
    internal CachedBitmapLease Add(
        string path,
        int width,
        int height,
        ImageOrientation orientation,
        ID2D1Bitmap1 bitmap)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long sizeBytes = DecodedImage.GetByteCount(width, height);
        var entry = new CachedBitmap(path, bitmap, width, height, orientation, sizeBytes);
        _entries.Add(path, _recentlyUsed.AddFirst(entry));
        _sizeBytes += sizeBytes;
        return Acquire(entry);
    }

    /// <summary>
    /// Any thread. Sets space aside for an image that is still loading, so that adding it later
    /// evicts nothing. Fails when the image doesn't fit next to what is cached and reserved.
    /// </summary>
    /// <remarks>
    /// Preloads use this, because they must never evict: a folder whose images do not all fit
    /// would throw one out to make room for the next and fetch it again a moment later.
    /// </remarks>
    internal bool TryReserve(long bytes)
    {
        long reserved = Volatile.Read(ref _reservedBytes);
        while (_capacityBytes - Volatile.Read(ref _sizeBytes) - reserved >= bytes)
        {
            long observed = Interlocked.CompareExchange(ref _reservedBytes, reserved + bytes, reserved);
            if (observed == reserved)
            {
                return true;
            }

            reserved = observed;
        }

        return false;
    }

    /// <summary>Any thread. Gives reserved space back, once its image is added or dropped.</summary>
    internal void Unreserve(long bytes) => Interlocked.Add(ref _reservedBytes, -bytes);

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

    /// <summary>
    /// Evicts the least recently used unleased bitmaps until they fit together with the
    /// reserved space, which a reserved image can then take without evicting anything itself.
    /// </summary>
    internal void Trim()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (_sizeBytes + Volatile.Read(ref _reservedBytes) > _capacityBytes)
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
