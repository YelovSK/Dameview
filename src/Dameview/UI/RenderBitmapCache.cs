using System.Diagnostics.CodeAnalysis;
using Dameview.Imaging;
using Vortice.Direct2D1;

namespace Dameview.UI;

// UI-thread owned. The cache owns every bitmap; the image panel only borrows
// Current.Bitmap while the entry is protected from eviction.
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
    internal CachedBitmap? Current { get; private set; }
    internal bool HasPreloadCapacity => _sizeBytes < CapacityBytes;

    internal bool Contains(string path) => _entries.ContainsKey(path);

    internal bool TryActivate(
        string path,
        [NotNullWhen(true)] out CachedBitmap? bitmap)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_entries.TryGetValue(path, out LinkedListNode<CachedBitmap>? node))
        {
            bitmap = null;
            return false;
        }

        Touch(node);
        Current = node.Value;
        bitmap = node.Value;
        return true;
    }

    internal CachedBitmap AddAndActivate(
        string path,
        ID2D1Bitmap1 bitmap,
        int width,
        int height)
    {
        CachedBitmap entry = Add(path, bitmap, width, height);
        Current = entry;
        return entry;
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

    internal void Deactivate()
    {
        Current = null;
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
            while (candidate is not null && ReferenceEquals(candidate.Value, Current))
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
        var node = _recentlyUsed.AddFirst(entry);
        _entries.Add(path, node);
        _sizeBytes += sizeBytes;
        return entry;
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
        Current = null;
        foreach (CachedBitmap entry in _recentlyUsed)
        {
            _disposeBitmap(entry.Bitmap);
        }

        _entries.Clear();
        _recentlyUsed.Clear();
        _sizeBytes = 0;
    }
}

internal sealed record CachedBitmap(
    string Path,
    ID2D1Bitmap1 Bitmap,
    int Width,
    int Height,
    long SizeBytes);

internal sealed class CachedBitmapRepresentation(CachedBitmap bitmap)
    : ImageRepresentation(bitmap.Width, bitmap.Height)
{
    internal CachedBitmap Bitmap { get; } = bitmap;
}
