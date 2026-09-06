using Dameview.Imaging;
using Dameview.UI.Panels;

namespace Dameview.Tests.UI;

[TestClass]
public sealed class TiledImageRendererTests
{
    [TestMethod]
    public void MipLevelTracksViewportResolution()
    {
        Assert.AreEqual(0, TiledImageRenderer.SelectMipLevel(1.0f));
        Assert.AreEqual(0, TiledImageRenderer.SelectMipLevel(0.51f));
        Assert.AreEqual(1, TiledImageRenderer.SelectMipLevel(0.5f));
        Assert.AreEqual(2, TiledImageRenderer.SelectMipLevel(0.25f));
        Assert.AreEqual(4, TiledImageRenderer.SelectMipLevel(0.05f));
        Assert.AreEqual(3_125, ImageTile.GetLevelDimension(50_000, 4));
        Assert.AreEqual(3_126, ImageTile.GetLevelDimension(50_001, 4));
        Assert.AreEqual(
            (X: 50_000, Y: 0, Width: 1, Height: 16),
            new ImageTile(3_125, 0, 1, 1, Level: 4).GetSourceBounds(50_001, 50_000));
    }
}
