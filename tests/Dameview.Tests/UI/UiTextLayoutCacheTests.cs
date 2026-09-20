using System.Drawing;
using Dameview.UI.Foundation;
using Vortice.DirectWrite;
using static Vortice.DirectWrite.DWrite;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class UiTextLayoutCacheTests
{
    private static readonly SizeF Box = new(120.0f, 24.0f);

    // The reason the cache exists: a redrawn frame must not reshape text that has not changed.
    [TestMethod]
    public void TextIsShapedOnceAndReusedUntilPartOfItsKeyChanges()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        using IDWriteTextFormat format = CreateFormat(factory, 14.0f);
        using IDWriteTextFormat larger = CreateFormat(factory, 18.0f);
        using var cache = new UiTextLayoutCache(factory);

        IDWriteTextLayout first = cache.Get("Tab one", format, Box);

        Assert.AreSame(first, cache.Get("Tab one", format, Box), "Redrawing unchanged text reuses it.");
        Assert.AreNotSame(first, cache.Get("Tab two", format, Box), "New text needs its own layout.");
        Assert.AreNotSame(first, cache.Get("Tab one", larger, Box), "So does a different format.");
        Assert.AreNotSame(first, cache.Get("Tab one", format, new SizeF(200.0f, 24.0f)), "So does a new box.");
        Assert.AreSame(first, cache.Get("Tab one", format, Box), "The original is still the original.");
    }

    [TestMethod]
    public void TextThatChangesEveryFrameCannotGrowTheCacheWithoutBound()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        using IDWriteTextFormat format = CreateFormat(factory, 14.0f);
        using var cache = new UiTextLayoutCache(factory);

        for (int frame = 0; frame < 5_000; frame++)
        {
            cache.Get($"{frame} %", format, Box);
        }

        Assert.IsLessThanOrEqualTo(512, cache.Count);
    }

    [TestMethod]
    public void TheLeastRecentlyDrawnTextIsTheFirstToGo()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        using IDWriteTextFormat format = CreateFormat(factory, 14.0f);
        using var cache = new UiTextLayoutCache(factory);
        IDWriteTextLayout closed = cache.Get("A tab that closed", format, Box);
        IDWriteTextLayout kept = cache.Get("Status bar", format, Box);

        // Enough new text to overflow by one, with only the status redrawn along the way.
        for (int index = 0; index < 511; index++)
        {
            cache.Get($"{index} %", format, Box);
            cache.Get("Status bar", format, Box);
        }

        Assert.AreSame(kept, cache.Get("Status bar", format, Box), "Text still being drawn stays.");
        Assert.AreNotSame(closed, cache.Get("A tab that closed", format, Box), "Text nobody draws goes.");
    }

    // Closing a pane disposes the formats its chrome owned, and a disposed COM object reports a
    // different hash than it did alive. Entries built with one have to stay findable, or they
    // can never be evicted and the cache fills up with text nothing draws any more.
    [TestMethod]
    public void EntriesWhoseFormatWasDisposedDoNotStrandTheCache()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        using var cache = new UiTextLayoutCache(factory);
        IDWriteTextFormat closed = CreateFormat(factory, 14.0f);
        for (int index = 0; index < 600; index++)
        {
            cache.Get($"a pane that closed {index}", closed, Box);
        }

        closed.Dispose();

        using IDWriteTextFormat live = CreateFormat(factory, 14.0f);
        IDWriteTextLayout drawn = cache.Get("still on screen", live, Box);

        Assert.IsLessThanOrEqualTo(512, cache.Count);
        Assert.AreSame(drawn, cache.Get("still on screen", live, Box), "The cache still caches.");
    }

    private static IDWriteTextFormat CreateFormat(IDWriteFactory factory, float size) =>
        factory.CreateTextFormat("Segoe UI", FontWeight.Normal, FontStyle.Normal, size);
}
