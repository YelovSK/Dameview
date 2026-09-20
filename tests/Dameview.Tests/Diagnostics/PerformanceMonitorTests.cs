using System.Diagnostics;
using Dameview.Diagnostics;

namespace Dameview.Tests.Diagnostics;

[TestClass]
public sealed class PerformanceMonitorTests
{
    [TestMethod]
    public void SnapshotSummarizesTheActiveFrameWindow()
    {
        var monitor = new PerformanceMonitor { Enabled = true };

        monitor.Record(Timing(0.0, 1.0));
        monitor.Record(Timing(2.0, 1.2));
        monitor.Record(Timing(4.0, 1.4));

        PerformanceSnapshot snapshot = monitor.Snapshot;
        Assert.AreEqual(2, snapshot.SampleCount);
        Assert.AreEqual(500.0, snapshot.FramesPerSecond, 0.01);
        Assert.AreEqual(2.0, snapshot.AverageFrameMilliseconds, 0.01);
        Assert.AreEqual(2.0, snapshot.MaximumFrameMilliseconds, 0.01);
        Assert.AreEqual(1.3, snapshot.AverageCpuMilliseconds, 0.01);
        Assert.AreEqual(1.4, snapshot.MaximumCpuMilliseconds, 0.01);
        Assert.AreEqual(0.6, snapshot.AverageGpuMilliseconds!.Value, 0.01);
        Assert.AreEqual(0.7, snapshot.MaximumGpuMilliseconds!.Value, 0.01);
    }

    [TestMethod]
    public void IdleGapStartsANewActiveFrameWindow()
    {
        var monitor = new PerformanceMonitor { Enabled = true };

        monitor.Record(Timing(0.0, 1.0));
        monitor.Record(Timing(2.0, 1.0));
        monitor.Record(Timing(1000.0, 1.0));

        Assert.AreEqual(0, monitor.Snapshot.SampleCount);

        monitor.Record(Timing(1002.0, 1.0));

        Assert.AreEqual(1, monitor.Snapshot.SampleCount);
        Assert.AreEqual(500.0, monitor.Snapshot.FramesPerSecond, 0.01);
    }

    [TestMethod]
    public void GpuAverageExcludesUnavailableSamples()
    {
        var monitor = new PerformanceMonitor { Enabled = true };
        monitor.Record(Timing(0, 1));
        monitor.Record(Timing(2, 1) with
        {
            GpuTime = TimeSpan.FromMilliseconds(2.0),
        });
        monitor.Record(Timing(4, 1) with { GpuTime = null });
        monitor.Record(Timing(6, 1) with
        {
            GpuTime = TimeSpan.FromMilliseconds(2.4),
        });

        Assert.AreEqual(2.2, monitor.Snapshot.AverageGpuMilliseconds!.Value, 0.001);
        Assert.AreEqual(2.4, monitor.Snapshot.MaximumGpuMilliseconds!.Value, 0.001);
    }

    // The overlay splits the frame into phases, and what it calls drawing is whatever the
    // other two did not account for.
    [TestMethod]
    public void DrawingIsWhateverTheOtherPhasesDidNotAccountFor()
    {
        var monitor = new PerformanceMonitor { Enabled = true };
        for (int frame = 0; frame < 3; frame++)
        {
            monitor.Record(Timing(frame * 10.0, 5.0) with
            {
                UpdateTime = TimeSpan.FromMilliseconds(0.5),
                LayoutTime = TimeSpan.FromMilliseconds(3.0),
                LayoutPasses = 1,
                AllocatedBytes = 1024,
            });
        }

        PerformanceSnapshot snapshot = monitor.Snapshot;

        Assert.AreEqual(0.5, snapshot.AverageUpdateMilliseconds, 0.001);
        Assert.AreEqual(3.0, snapshot.AverageLayoutMilliseconds, 0.001);
        Assert.AreEqual(1.5, snapshot.AverageDrawMilliseconds, 0.001);
    }

    // A frame whose phases somehow add up to more than the whole must not report negative drawing.
    [TestMethod]
    public void PhasesThatOverrunTheFrameReportNoDrawingRatherThanNegative()
    {
        var monitor = new PerformanceMonitor { Enabled = true };
        monitor.Record(Timing(0.0, 1.0));
        monitor.Record(Timing(10.0, 1.0) with { LayoutTime = TimeSpan.FromMilliseconds(9.0) });

        Assert.AreEqual(0.0, monitor.Snapshot.AverageDrawMilliseconds, 0.001);
    }

    private static PerformanceFrameTiming Timing(double startedMilliseconds, double cpuMilliseconds) =>
        new(
            (long)(startedMilliseconds * Stopwatch.Frequency / 1000.0),
            TimeSpan.FromMilliseconds(cpuMilliseconds),
            TimeSpan.FromMilliseconds(startedMilliseconds / 10.0 + 0.3));
}
