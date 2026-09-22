using System.Drawing;
using Dameview.UI.Foundation;
using Vortice.DirectWrite;
using static Vortice.DirectWrite.DWrite;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class UiTextLayoutCacheTests
{
    private static readonly SizeF Box = new(120.0f, 24.0f);
    private static readonly UiFont Font = new(14.0f);

    // The reason the cache exists: a redrawn frame must not reshape text that has not changed.
    [TestMethod]
    public void TextIsShapedOnceAndReusedUntilPartOfItsKeyChanges()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        using var cache = new UiTextLayoutCache(factory);

        IDWriteTextLayout first = cache.Get("Tab one", Font, Box);

        Assert.AreSame(first, cache.Get("Tab one", Font, Box), "Redrawing unchanged text reuses it.");
        Assert.AreNotSame(first, cache.Get("Tab two", Font, Box), "New text needs its own layout.");
        Assert.AreNotSame(first, cache.Get("Tab one", Font with { Size = 18.0f }, Box), "So does a different font.");
        Assert.AreNotSame(first, cache.Get("Tab one", Font, new SizeF(200.0f, 24.0f)), "So does a new box.");
        Assert.AreSame(first, cache.Get("Tab one", Font, Box), "The original is still the original.");
    }

    [TestMethod]
    public void TextThatChangesEveryFrameCannotGrowTheCacheWithoutBound()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        using var cache = new UiTextLayoutCache(factory);

        for (int frame = 0; frame < 5_000; frame++)
        {
            cache.Get($"{frame} %", Font, Box);
        }

        Assert.IsLessThanOrEqualTo(512, cache.Count);
    }

    [TestMethod]
    public void TheLeastRecentlyDrawnTextIsTheFirstToGo()
    {
        using IDWriteFactory1 factory = DWriteCreateFactory<IDWriteFactory1>();
        using var cache = new UiTextLayoutCache(factory);
        IDWriteTextLayout closed = cache.Get("A tab that closed", Font, Box);
        IDWriteTextLayout kept = cache.Get("Status bar", Font, Box);

        // Enough new text to overflow by one, with only the status redrawn along the way.
        for (int index = 0; index < 511; index++)
        {
            cache.Get($"{index} %", Font, Box);
            cache.Get("Status bar", Font, Box);
        }

        Assert.AreSame(kept, cache.Get("Status bar", Font, Box), "Text still being drawn stays.");
        Assert.AreNotSame(closed, cache.Get("A tab that closed", Font, Box), "Text nobody draws goes.");
    }
}
