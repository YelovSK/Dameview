using Dameview.Updates;

namespace Dameview.Tests.Updates;

[TestClass]
public sealed class AppUpdateApplierTests
{
    [TestMethod]
    public void OrdinaryLaunchArgumentsAreNotUpdateRequests()
    {
        Assert.IsFalse(AppUpdateApplier.TryApply([@"C:\Images\photo.jpg"]));
        Assert.IsFalse(AppUpdateApplier.TryApply(["--apply-update", "not-a-process-id"]));
        Assert.IsFalse(AppUpdateApplier.TryApply(["--apply-update", "123", @"C:\missing.exe"]));
    }
}
