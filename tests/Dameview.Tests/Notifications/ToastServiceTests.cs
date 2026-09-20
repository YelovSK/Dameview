using Dameview.Notifications;

namespace Dameview.Tests.Notifications;

[TestClass]
public sealed class ToastServiceTests
{
    [TestMethod]
    public void NotifyingRaisesChangedAndKeepsTheMessage()
    {
        var time = new ManualTimeProvider();
        var service = new ToastService(time);
        int changes = 0;
        service.Changed += () => changes++;

        service.Notify("Could not copy the image.", ToastSeverity.Error);

        Assert.AreEqual(1, changes);
        Toast toast = service.Toasts.Single();
        Assert.AreEqual("Could not copy the image.", toast.Message);
        Assert.AreEqual(ToastSeverity.Error, toast.Severity);
    }

    [TestMethod]
    public void MessagesExpireOnlyOnceTheirTimeIsUp()
    {
        var time = new ManualTimeProvider();
        var service = new ToastService(time);
        service.Notify("Saved.", ToastSeverity.Success);

        time.Advance(ToastService.Lifetime - TimeSpan.FromMilliseconds(1));
        Assert.IsFalse(service.RemoveExpired());
        Assert.AreEqual(1, service.Toasts.Count);

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(service.RemoveExpired());
        Assert.AreEqual(0, service.Toasts.Count);
        Assert.IsFalse(service.RemoveExpired(), "An empty service has nothing left to announce.");
    }

    [TestMethod]
    public void EachMessageExpiresOnItsOwnSchedule()
    {
        var time = new ManualTimeProvider();
        var service = new ToastService(time);
        service.Notify("First.", ToastSeverity.Warning);
        time.Advance(TimeSpan.FromSeconds(3));
        service.Notify("Second.", ToastSeverity.Warning);

        time.Advance(ToastService.Lifetime - TimeSpan.FromSeconds(3));

        Assert.IsTrue(service.RemoveExpired());
        Assert.AreEqual("Second.", service.Toasts.Single().Message);
    }

    [TestMethod]
    public void DismissingRemovesOnlyThatMessage()
    {
        var time = new ManualTimeProvider();
        var service = new ToastService(time);
        service.Notify("First.", ToastSeverity.Error);
        service.Notify("Second.", ToastSeverity.Error);
        long first = service.Toasts[0].Id;
        int changes = 0;
        service.Changed += () => changes++;

        service.Dismiss(first);
        service.Dismiss(first);

        Assert.AreEqual(1, changes, "Dismissing something already gone announces nothing.");
        Assert.AreEqual("Second.", service.Toasts.Single().Message);
    }

    [TestMethod]
    public void TheOldestMessagesGiveWayOnceTheStackIsFull()
    {
        var time = new ManualTimeProvider();
        var service = new ToastService(time);
        for (int index = 0; index < 6; index++)
        {
            service.Notify($"Message {index}.", ToastSeverity.Warning);
        }

        Assert.AreEqual(4, service.Toasts.Count);
        Assert.AreEqual("Message 2.", service.Toasts[0].Message);
        Assert.AreEqual("Message 5.", service.Toasts[^1].Message);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        internal void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
    }
}
