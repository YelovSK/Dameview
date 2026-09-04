using Dameview.UI.Animation;

namespace Dameview.Tests.UI.Animation;

[TestClass]
public sealed class AnimatedFloatTests
{
    [TestMethod]
    public void UpdateApproachesTheTargetAndEventuallyCompletes()
    {
        var value = new AnimatedFloat(0.0f, 10.0);

        Assert.IsTrue(value.SetTarget(1.0f));
        Assert.IsTrue(value.Update(0.1));
        Assert.AreEqual(0.6321f, value.Current, 0.0001f);

        for (int frame = 0; frame < 100 && value.Update(1.0 / 60.0); frame++)
        {
        }

        Assert.AreEqual(1.0f, value.Current);
        Assert.IsFalse(value.Update(1.0 / 60.0));
    }

    [TestMethod]
    public void SettingTheExistingTargetDoesNotStartAnotherAnimation()
    {
        var value = new AnimatedFloat(1.0f, 10.0);

        Assert.IsFalse(value.SetTarget(1.0f));
        Assert.IsFalse(value.Update(0.1));
    }
}
