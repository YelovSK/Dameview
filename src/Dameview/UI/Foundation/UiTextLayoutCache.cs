using System.Drawing;
using System.Runtime.CompilerServices;
using Vortice.DirectWrite;

namespace Dameview.UI.Foundation;

/// <summary>Holds the shaped text layouts drawn in recent frames, keyed by what they are made of.</summary>
/// <remarks>
/// <para>
/// Direct2D's <c>DrawText</c> shapes the string and builds a throwaway layout on every call, which
/// a retained-mode UI pays for on every frame it redraws, for every string on screen. Caching them
/// here lets an unchanged string skip that work without every caller holding a layout of its own.
/// Text that differs on every frame costs what it always did, plus an entry that ages back out.
/// </para>
/// <para>
/// A layout handed out here belongs to the cache, which is free to evict it to make room for a
/// later one, so callers draw or measure with it and ask again rather than holding on to it. What
/// makes that safe is the capacity: eviction takes the least recently used entry, so nothing a
/// frame has already asked for can be thrown away while that frame is still drawing.
/// </para>
/// </remarks>
internal sealed class UiTextLayoutCache : IDisposable
{
    // Far more than a frame draws, so eviction only reaches text that has genuinely gone away.
    private const int Capacity = 512;

    private readonly IDWriteFactory _factory;
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _recentlyUsed = new();

    internal UiTextLayoutCache(IDWriteFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    internal int Count => _entries.Count;

    internal IDWriteTextLayout Get(string text, IDWriteTextFormat format, SizeF maxSize)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(format);
        var key = new Key(text, format, maxSize.Width, maxSize.Height);
        if (_entries.TryGetValue(key, out LinkedListNode<Entry>? cached))
        {
            _recentlyUsed.Remove(cached);
            _recentlyUsed.AddFirst(cached);
            return cached.Value.Layout;
        }

        IDWriteTextLayout layout = _factory.CreateTextLayout(
            text, format, maxSize.Width, maxSize.Height);
        _entries.Add(key, _recentlyUsed.AddFirst(new Entry(key, layout)));
        Trim();
        return layout;
    }

    public void Dispose()
    {
        foreach (Entry entry in _recentlyUsed)
        {
            entry.Layout.Dispose();
        }

        _recentlyUsed.Clear();
        _entries.Clear();
    }

    private void Trim()
    {
        while (_entries.Count > Capacity)
        {
            LinkedListNode<Entry> oldest = _recentlyUsed.Last!;
            _recentlyUsed.Remove(oldest);
            _entries.Remove(oldest.Value.Key);
            oldest.Value.Layout.Dispose();
        }
    }

    // The format shapes the text as much as the string does, so two layouts that differ only by
    // font or alignment must not share an entry. It is keyed by its managed identity rather than
    // itself, because a disposed COM object reports a different hash than it did alive and
    // compares equal to every other disposed one, which would strand its entry here forever.
    private readonly struct Key(string text, IDWriteTextFormat format, float width, float height)
        : IEquatable<Key>
    {
        private readonly string _text = text;
        private readonly IDWriteTextFormat _format = format;
        private readonly float _width = width;
        private readonly float _height = height;

        public bool Equals(Key other) =>
            ReferenceEquals(_format, other._format)
            && _width.Equals(other._width)
            && _height.Equals(other._height)
            && _text == other._text;

        public override bool Equals(object? obj) => obj is Key other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(_text, RuntimeHelpers.GetHashCode(_format), _width, _height);
    }

    private readonly record struct Entry(Key Key, IDWriteTextLayout Layout);
}
