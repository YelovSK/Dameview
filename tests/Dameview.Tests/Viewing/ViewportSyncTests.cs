using System.Drawing;
using System.Numerics;
using Dameview.Viewing;

namespace Dameview.Tests.Viewing;

[TestClass]
public sealed class ViewportSyncTests
{
    [TestMethod]
    public void FollowersShowTheSameSpotAtTheSameSizeWhateverTheirResolution()
    {
        ImageViewport large = CreateViewport(new Size(2000, 1000));
        ImageViewport small = CreateViewport(new Size(1000, 500));
        var sync = new ViewportSync();
        sync.Sync([large, small], large);

        large.SetScaleAt(1.0f, large.ViewportCenter, new PointF(1500.0f, 300.0f));
        sync.Sync([large, small], large);

        Assert.AreEqual(2.0f, small.Scale);
        Assert.AreEqual(new PointF(750.0f, 150.0f), small.Center);
    }

    [TestMethod]
    public void WhicheverViewportMovedLeads()
    {
        ImageViewport active = CreateViewport(new Size(2000, 1000));
        ImageViewport other = CreateViewport(new Size(2000, 1000));
        var sync = new ViewportSync();
        active.SetScaleAt(1.0f, active.ViewportCenter, active.ImageCenter);
        sync.Sync([active, other], active);

        other.PanBy(new Vector2(200.0f, 0.0f));
        sync.Sync([active, other], active);

        Assert.AreEqual(other.Center, active.Center);
    }

    [TestMethod]
    public void AViewportThatJoinsLinesUpWithThePreferredOne()
    {
        ImageViewport active = CreateViewport(new Size(2000, 1000));
        ImageViewport joining = CreateViewport(new Size(2000, 1000));
        var sync = new ViewportSync();
        sync.Sync([active], active);
        active.SetScaleAt(1.0f, active.ViewportCenter, new PointF(600.0f, 400.0f));
        sync.Sync([active], active);

        sync.Sync([active, joining], active);

        Assert.AreEqual(active.Scale, joining.Scale);
        Assert.AreEqual(active.Center, joining.Center);
    }

    private static ImageViewport CreateViewport(Size imageSize)
    {
        var viewport = new ImageViewport(new Size(800, 600));
        viewport.SetImageSize(imageSize);
        return viewport;
    }
}
