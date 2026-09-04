using System.Diagnostics.CodeAnalysis;

namespace Dameview.Imaging;

internal sealed class DecodedImageCache
{
    private readonly object _sync = new();
    private readonly long _capacityBytes;
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<Entry> _recentlyUsed = new();
    private long _sizeBytes;

    internal DecodedImageCache(long capacityBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacityBytes);
        _capacityBytes = capacityBytes;
    }

    internal bool TryGet(
        string path,
        [NotNullWhen(true)] out DecodedImage? image)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(path, out LinkedListNode<Entry>? node))
            {
                image = null;
                return false;
            }

            _recentlyUsed.Remove(node);
            _recentlyUsed.AddFirst(node);
            image = node.Value.Image;
            return true;
        }
    }

    internal void Add(string path, DecodedImage image)
    {
        long imageSize = image.Pixels.LongLength;
        if (imageSize > _capacityBytes)
        {
            return;
        }

        lock (_sync)
        {
            if (_entries.Remove(path, out LinkedListNode<Entry>? existingNode))
            {
                _recentlyUsed.Remove(existingNode);
                _sizeBytes -= existingNode.Value.SizeBytes;
            }

            var node = new LinkedListNode<Entry>(new Entry(path, image, imageSize));
            _recentlyUsed.AddFirst(node);
            _entries.Add(path, node);
            _sizeBytes += imageSize;

            while (_sizeBytes > _capacityBytes)
            {
                LinkedListNode<Entry> oldest = _recentlyUsed.Last!;
                _recentlyUsed.RemoveLast();
                _entries.Remove(oldest.Value.Path);
                _sizeBytes -= oldest.Value.SizeBytes;
            }
        }
    }

    private sealed record Entry(
        string Path,
        DecodedImage Image,
        long SizeBytes);
}
