using Dameview.UI.Animation;
using Dameview.UI.Foundation;

namespace Dameview.Tests.UI.Animation;

[TestClass]
public sealed class AnimatedFloatTests
{
    private static readonly UiUpdateContext Frame = new(1.0 / 60.0);

    [TestMethod]
    public void UpdateApproachesTheTargetAndEventuallyCompletes()
    {
        var value = new AnimatedFloat(0.0f, Motion.Smooth);

        Assert.IsTrue(value.SetTarget(1.0f));
        Assert.IsTrue(value.Update(Frame));
        Assert.IsTrue(value.Current is > 0.0f and < 1.0f);

        for (int frame = 0; frame < 200 && value.Update(Frame); frame++)
        {
        }

        Assert.AreEqual(1.0f, value.Current);
        Assert.IsFalse(value.Update(Frame));
    }

    [TestMethod]
    public void FullDampingNeverPassesTheTarget()
    {
        var value = new AnimatedFloat(0.0f, Motion.Smooth);
        value.SetTarget(1.0f);

        while (value.Update(Frame))
        {
            Assert.IsTrue(value.Current <= 1.0f);
        }
    }

    [TestMethod]
    public void LowDampingOvershootsAndSettlesBack()
    {
        var value = new AnimatedFloat(0.0f, Motion.Bouncy);
        value.SetTarget(1.0f);

        float highest = 0.0f;
        while (value.Update(Frame))
        {
            highest = MathF.Max(highest, value.Current);
        }

        Assert.IsTrue(highest > 1.05f);
        Assert.AreEqual(1.0f, value.Current);
    }

    [TestMethod]
    public void ChangingTheTargetKeepsTheSpeed()
    {
        var value = new AnimatedFloat(0.0f, Motion.Bouncy);
        value.SetTarget(1.0f);
        for (int frame = 0; frame < 5; frame++)
        {
            value.Update(Frame);
        }

        float before = value.Current;
        value.SetTarget(0.0f);
        value.Update(Frame);

        Assert.IsTrue(value.Current > before);
    }

    [TestMethod]
    public void SettingTheExistingTargetDoesNotStartAnotherAnimation()
    {
        var value = new AnimatedFloat(1.0f, Motion.Snappy);

        Assert.IsFalse(value.SetTarget(1.0f));
        Assert.IsFalse(value.Update(new UiUpdateContext(0.1)));
    }

    [TestMethod]
    public void DisabledAnimationsSnapToTheTarget()
    {
        var value = new AnimatedFloat(0.0f, Motion.Snappy);
        value.SetTarget(1.0f);

        Assert.IsFalse(value.Update(new UiUpdateContext(0.0, AnimationsEnabled: false)));
        Assert.AreEqual(1.0f, value.Current);
    }
}
