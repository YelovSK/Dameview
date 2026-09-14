using System.Collections.Concurrent;
using Dameview.Platform;

namespace Dameview.Tests.Platform;

[TestClass]
public sealed class BackgroundQueueTests
{
    [TestMethod]
    public void WorkRunsOffTheCallingThreadAndReturnsItsResult()
    {
        int callerThread = Environment.CurrentManagedThreadId;
        int workThread = 0;
        using var queue = new ComWorkerQueue<object?>("test", 1, () => null);

        int result = queue.Enqueue((_, _) =>
        {
            workThread = Environment.CurrentManagedThreadId;
            return 42;
        }).GetAwaiter().GetResult();

        Assert.AreEqual(42, result);
        Assert.AreNotEqual(callerThread, workThread);
    }

    [TestMethod]
    public void EachWorkerOwnsOneContextInstance()
    {
        int created = 0;
        using var queue = new ComWorkerQueue<int>("test", 2, () => Interlocked.Increment(ref created));
        var contexts = new ConcurrentBag<int>();
        using var start = new ManualResetEventSlim();
        using var done = new CountdownEvent(2);

        for (int index = 0; index < 2; index++)
        {
            _ = queue.Enqueue((context, _) =>
            {
                contexts.Add(context);
                start.Wait();
                done.Signal();
            });
        }

        start.Set();

        Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(2, created);
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, contexts.ToArray());
    }

    [TestMethod]
    public void HigherPriorityWorkRunsFirst()
    {
        using var queue = new ComWorkerQueue<object?>("test", 1, () => null);
        using var gate = new ManualResetEventSlim();
        using var blockerStarted = new ManualResetEventSlim();
        using var done = new CountdownEvent(3);
        var order = new ConcurrentQueue<int>();

        _ = queue.Enqueue((_, _) =>
        {
            blockerStarted.Set();
            gate.Wait();
        });
        Assert.IsTrue(blockerStarted.Wait(TimeSpan.FromSeconds(5)));

        _ = queue.Enqueue((_, _) => { order.Enqueue(1); done.Signal(); }, priority: 1);
        _ = queue.Enqueue((_, _) => { order.Enqueue(2); done.Signal(); }, priority: 5);
        _ = queue.Enqueue((_, _) => { order.Enqueue(3); done.Signal(); }, priority: 3);

        gate.Set();

        Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(5)));
        CollectionAssert.AreEqual(new[] { 2, 3, 1 }, order.ToArray());
    }

    [TestMethod]
    public void CancelledQueuedWorkIsNotExecuted()
    {
        using var queue = new ComWorkerQueue<object?>("test", 1, () => null);
        using var gate = new ManualResetEventSlim();
        using var blockerStarted = new ManualResetEventSlim();

        _ = queue.Enqueue((_, _) =>
        {
            blockerStarted.Set();
            gate.Wait();
        });
        Assert.IsTrue(blockerStarted.Wait(TimeSpan.FromSeconds(5)));

        using var cancellation = new CancellationTokenSource();
        bool executed = false;
        Task<int> pending = queue.Enqueue(
            (_, _) => { executed = true; return 1; },
            cancellationToken: cancellation.Token);
        cancellation.Cancel();

        gate.Set();

        Assert.ThrowsExactly<TaskCanceledException>(() => pending.GetAwaiter().GetResult());
        Assert.IsFalse(executed);
    }

    [TestMethod]
    public void DisposeCancelsPendingWork()
    {
        using var queue = new ComWorkerQueue<object?>("test", 1, () => null);
        using var gate = new ManualResetEventSlim();
        using var blockerStarted = new ManualResetEventSlim();

        _ = queue.Enqueue((_, _) =>
        {
            blockerStarted.Set();
            gate.Wait();
        });
        Assert.IsTrue(blockerStarted.Wait(TimeSpan.FromSeconds(5)));

        Task<int> pending = queue.Enqueue((_, _) => 1);
        queue.Dispose();

        Assert.ThrowsExactly<TaskCanceledException>(() => pending.GetAwaiter().GetResult());
        gate.Set();
    }

    [TestMethod]
    public void WorkExceptionsSurfaceThroughTheTask()
    {
        using var queue = new ComWorkerQueue<object?>("test", 1, () => null);

        Task<int> task = queue.Enqueue<int>((_, _) => throw new InvalidOperationException("boom"));

        Assert.ThrowsExactly<InvalidOperationException>(() => task.GetAwaiter().GetResult());
    }

    [TestMethod]
    public void ContextCreationFailureFaultsPendingWork()
    {
        using var queue = new ComWorkerQueue<object?>(
            "test",
            1,
            () => throw new InvalidOperationException("no context"));

        Task<int> task = queue.Enqueue((_, _) => 1);

        Assert.ThrowsExactly<InvalidOperationException>(() => task.GetAwaiter().GetResult());
    }
}
