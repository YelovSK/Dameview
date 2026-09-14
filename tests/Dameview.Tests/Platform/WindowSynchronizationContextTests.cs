using Dameview.Platform;

namespace Dameview.Tests.Platform;

[TestClass]
public sealed class WindowSynchronizationContextTests
{
    [TestMethod]
    public void PostDefersThroughTheSuppliedDispatcher()
    {
        var actions = new Queue<Action>();
        var context = new WindowSynchronizationContext(actions.Enqueue);
        bool ran = false;

        context.Post(_ => ran = true, null);

        Assert.AreEqual(1, actions.Count);
        Assert.IsFalse(ran);
        actions.Dequeue()();
        Assert.IsTrue(ran);
    }

    [TestMethod]
    public void PostPassesStateToTheCallback()
    {
        var actions = new Queue<Action>();
        var context = new WindowSynchronizationContext(actions.Enqueue);
        object? received = null;

        context.Post(state => received = state, "payload");
        actions.Dequeue()();

        Assert.AreEqual("payload", received);
    }
}
