using Dameview.Updates;

namespace Dameview.Tests.Updates;

[TestClass]
public sealed class AppReleaseTests
{
    [TestMethod]
    public void ReleaseTagIsParsedAndNormalized()
    {
        var release = AppRelease.FromTag("v1.2.3");

        Assert.IsNotNull(release);
        Assert.AreEqual("v1.2.3", release.Tag);
        Assert.AreEqual(new Version(1, 2, 3, 0), release.Version);
    }

    [TestMethod]
    public void InvalidReleaseTagIsRejected()
    {
        Assert.IsNull(AppRelease.FromTag("latest"));
    }
}
