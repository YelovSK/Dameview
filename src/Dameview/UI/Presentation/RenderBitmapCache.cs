using System.Diagnostics.CodeAnalysis;
using Dameview.Imaging.Loading;
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
        CapacityBytes = capacityBytes;
        _disposeBitmap = disposeBitmap;
    }

    internal long CapacityBytes { get; }
    /// <summary>Whether anything speculative is worth fetching, before its size is known.</summary>
    internal bool HasPreloadCapacity => _sizeBytes < CapacityBytes;

    /// <summary>
    /// Whether an image of this size can be kept speculatively.
    /// </summary>
    /// <remarks>
    /// A preload may use free space but must never evict, or a folder whose images do not all
    /// fit would throw one out to make room for the next and fetch it again a moment later.
    /// </remarks>
    internal bool CanPreload(int width, int height) =>
        _sizeBytes + ((long)width * height * 4) <= CapacityBytes;

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

    internal CachedBitmapLease AddAndAcquire(
        string path,
        ID2D1Bitmap1 bitmap,
        int width,
        int height)
    {
        return Acquire(Add(path, bitmap, width, height));
    }

    internal void AddInactive(
        string path,
        ID2D1Bitmap1 bitmap,
        int width,
        int height)
    {
        _ = Add(path, bitmap, width, height);
        Trim();
    }

    internal void DisposeUncached(ID2D1Bitmap1 bitmap)
    {
        _disposeBitmap(bitmap);
    }

    internal void Trim()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (_sizeBytes > CapacityBytes)
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

    private CachedBitmap Add(string path, ID2D1Bitmap1 bitmap, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_entries.TryGetValue(path, out LinkedListNode<CachedBitmap>? existing))
        {
            _disposeBitmap(bitmap);
            Touch(existing);
            return existing.Value;
        }

        long sizeBytes = checked((long)width * height * 4);
        var entry = new CachedBitmap(path, bitmap, width, height, sizeBytes);
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
        foreach (CachedBitmap entry in _recentlyUsed)
        {
            _disposeBitmap(entry.Bitmap);
        }

        _entries.Clear();
        _recentlyUsed.Clear();
        _sizeBytes = 0;
    }
}

internal sealed class CachedBitmap(
    string path,
    ID2D1Bitmap1 bitmap,
    int width,
    int height,
    long sizeBytes)
{
    internal string Path { get; } = path;
    internal ID2D1Bitmap1 Bitmap { get; } = bitmap;
    internal int Width { get; } = width;
    internal int Height { get; } = height;
    internal long SizeBytes { get; } = sizeBytes;
    internal int PinCount { get; set; }
}

internal sealed class CachedBitmapLease(
    RenderBitmapCache owner,
    CachedBitmap bitmap) : IDisposable
{
    private RenderBitmapCache? _owner = owner;

    internal CachedBitmap Bitmap { get; } = bitmap;

    public void Dispose()
    {
        RenderBitmapCache? currentOwner = _owner;
        _owner = null;
        currentOwner?.Release(Bitmap);
    }
}

internal sealed class CachedBitmapRepresentation : ImageRepresentation
{
    private readonly CachedBitmapLease _lease;

    internal CachedBitmapRepresentation(CachedBitmapLease lease)
        : base(lease.Bitmap.Width, lease.Bitmap.Height)
    {
        _lease = lease;
    }

    internal ID2D1Bitmap1 Bitmap => _lease.Bitmap.Bitmap;

    protected override void DisposeCore() => _lease.Dispose();
}
