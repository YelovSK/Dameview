using System.Drawing;
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
/// <para>
/// The formats the layouts are built from live here too, one per distinct <see cref="UiFont"/>.
/// The UI uses only a handful, so they are kept until the cache is disposed.
/// </para>
/// </remarks>
internal sealed class UiTextLayoutCache : IDisposable
{
    // Far more than a frame draws, so eviction only reaches text that has genuinely gone away.
    private const int Capacity = 512;

    private readonly IDWriteFactory _factory;
    private readonly Dictionary<UiFont, Format> _formats = [];
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _recentlyUsed = new();

    internal UiTextLayoutCache(IDWriteFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    internal int Count => _entries.Count;

    internal IDWriteTextLayout Get(string text, UiFont font, SizeF maxSize)
    {
        ArgumentNullException.ThrowIfNull(text);
        var key = new Key(text, font, maxSize.Width, maxSize.Height);
        if (_entries.TryGetValue(key, out LinkedListNode<Entry>? cached))
        {
            _recentlyUsed.Remove(cached);
            _recentlyUsed.AddFirst(cached);
            return cached.Value.Layout;
        }

        IDWriteTextLayout layout = _factory.CreateTextLayout(
            text, GetFormat(font), maxSize.Width, maxSize.Height);
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
        foreach (Format format in _formats.Values)
        {
            format.TextFormat.Dispose();
            format.EllipsisSign?.Dispose();
        }

        _formats.Clear();
    }

    private IDWriteTextFormat GetFormat(UiFont font)
    {
        if (_formats.TryGetValue(font, out Format cached))
        {
            return cached.TextFormat;
        }

        IDWriteTextFormat format = _factory.CreateTextFormat(
            font.Family, font.Weight, FontStyle.Normal, font.Size);
        format.TextAlignment = font.Alignment;
        format.ParagraphAlignment = font.VerticalAlignment;
        format.WordWrapping = font.Wrapping;
        if (font.TabStop is float tabStop)
        {
            format.IncrementalTabStop = tabStop;
        }

        IDWriteInlineObject? ellipsisSign = null;
        if (font.Ellipsis)
        {
            ellipsisSign = _factory.CreateEllipsisTrimmingSign(format);
            format.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ellipsisSign);
        }

        _formats.Add(font, new Format(format, ellipsisSign));
        return format;
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

    private readonly record struct Key(string Text, UiFont Font, float Width, float Height);

    private readonly record struct Entry(Key Key, IDWriteTextLayout Layout);

    private readonly record struct Format(IDWriteTextFormat TextFormat, IDWriteInlineObject? EllipsisSign);
}
