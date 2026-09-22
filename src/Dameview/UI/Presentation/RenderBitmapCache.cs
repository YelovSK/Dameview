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

    /// <summary>
    /// Disposes every cached bitmap, as the graphics device they belong to is going away.
    /// An outstanding lease keeps its entry alive but its bitmap is gone, so whatever is being
    /// displayed has to be dropped in the same pass.
    /// </summary>
    internal void Clear()
    {
        foreach (CachedBitmap entry in _recentlyUsed)
        {
            _disposeBitmap(entry.Bitmap);
        }

        _entries.Clear();
        _recentlyUsed.Clear();
        _sizeBytes = 0;
    }

    /// <summary>
    /// Reads every cached bitmap into system memory and releases it, because the device that
    /// owns it is being replaced. A cached bitmap is the only image with no copy outside the
    /// GPU, so this is what keeps a device switch from decoding everything again. Entries keep
    /// their identity, so outstanding leases stay valid across the pair.
    /// </summary>
    internal void ReadBack(ID2D1DeviceContext deviceContext)
    {
        // Only what is on screen is worth moving. Everything else was fetched speculatively,
        // so dropping it costs a background reload that nobody sees, while moving it would
        // stall the window thread for as long as it takes to copy whole images twice.
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

    /// <summary>Puts the read-back bitmaps on the replacement device.</summary>
    internal void Upload(ID2D1DeviceContext deviceContext)
    {
        foreach (CachedBitmap entry in _recentlyUsed)
        {
            entry.Bitmap = D2DBitmapFactory.Create(deviceContext, entry.Pixels!);
            entry.Pixels = null;
        }
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
        Clear();
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
    // Replaced when the entry moves to another device, which is why leases read through here.
    internal ID2D1Bitmap1 Bitmap { get; set; } = bitmap;

    /// <summary>Held only between reading the bitmap back and uploading it again.</summary>
    internal DecodedImage? Pixels { get; set; }
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
