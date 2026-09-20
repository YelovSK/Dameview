using System.Diagnostics;

namespace Dameview.Diagnostics;

internal readonly record struct PerformanceFrameTiming(
    long FrameStartedTimestamp,
    TimeSpan CpuTime,
    TimeSpan? GpuTime,
    TimeSpan UpdateTime = default,
    TimeSpan LayoutTime = default,
    int LayoutPasses = 0,
    long AllocatedBytes = 0,
    int DrawnElements = 0,
    int DrawOperations = 0,
    TimeSpan DrawTime = default,
    TimeSpan SubmitTime = default,
    (long Used, long Budget)? VideoMemory = null,
    (long WorkingSet, long ManagedHeap)? Memory = null);

internal readonly record struct PerformanceSnapshot(
    int SampleCount,
    double FramesPerSecond,
    double AverageFrameMilliseconds,
    double MaximumFrameMilliseconds,
    double AverageCpuMilliseconds,
    double MaximumCpuMilliseconds,
    double? AverageGpuMilliseconds,
    double? MaximumGpuMilliseconds,
    double AverageUpdateMilliseconds,
    double AverageLayoutMilliseconds,
    double AverageDrawMilliseconds,
    double LayoutPassesPerSecond,
    double AllocatedBytesPerSecond,
    double AverageDrawnElements,
    double AverageDrawOperations,
    double AverageSubmitMilliseconds,
    double AverageOtherMilliseconds,
    (long Used, long Budget)? VideoMemory,
    (long WorkingSet, long ManagedHeap)? Memory);

internal sealed class PerformanceMonitor
{
    private const int MaximumSamples = 2048;
    private static readonly long SampleWindowTicks = (long)(Stopwatch.Frequency * 1.0);
    private static readonly long IdleResetTicks = (long)(Stopwatch.Frequency * 0.25);

    private readonly Sample[] _samples = new Sample[MaximumSamples];
    private int _nextSample;
    private int _sampleCount;
    private (long Used, long Budget)? _videoMemory;
    private (long WorkingSet, long ManagedHeap)? _memory;
    private long _previousFrameStarted;
    private bool _hasPreviousFrame;

    internal bool Enabled
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            ClearSamples();
            _hasPreviousFrame = false;
        }
    }

    internal PerformanceSnapshot Snapshot => CalculateSnapshot();

    internal void Record(in PerformanceFrameTiming timing)
    {
        if (!Enabled)
        {
            return;
        }

        // Levels rather than rates, so the latest reading stands on its own.
        _videoMemory = timing.VideoMemory ?? _videoMemory;
        _memory = timing.Memory ?? _memory;

        if (_hasPreviousFrame)
        {
            long intervalTicks = timing.FrameStartedTimestamp - _previousFrameStarted;
            if (intervalTicks <= 0 || intervalTicks > IdleResetTicks)
            {
                ClearSamples();
            }
            else
            {
                AddSample(new Sample(
                    timing.FrameStartedTimestamp,
                    intervalTicks * 1000.0 / Stopwatch.Frequency,
                    timing.CpuTime.TotalMilliseconds,
                    timing.GpuTime?.TotalMilliseconds,
                    timing.UpdateTime.TotalMilliseconds,
                    timing.LayoutTime.TotalMilliseconds,
                    timing.LayoutPasses,
                    timing.AllocatedBytes,
                    timing.DrawnElements,
                    timing.DrawOperations,
                    timing.DrawTime.TotalMilliseconds,
                    timing.SubmitTime.TotalMilliseconds));
            }
        }

        _previousFrameStarted = timing.FrameStartedTimestamp;
        _hasPreviousFrame = true;
        RemoveExpiredSamples(timing.FrameStartedTimestamp);
    }

    private void AddSample(Sample sample)
    {
        _samples[_nextSample] = sample;
        _nextSample = (_nextSample + 1) % MaximumSamples;
        _sampleCount = Math.Min(_sampleCount + 1, MaximumSamples);
    }

    private void RemoveExpiredSamples(long now)
    {
        while (_sampleCount > 0)
        {
            int oldestIndex = (_nextSample - _sampleCount + MaximumSamples) % MaximumSamples;
            if (now - _samples[oldestIndex].FrameStartedTimestamp <= SampleWindowTicks)
            {
                break;
            }

            _sampleCount--;
        }
    }

    private PerformanceSnapshot CalculateSnapshot()
    {
        if (_sampleCount == 0)
        {
            return default;
        }

        double frameTotal = 0.0;
        double frameMaximum = 0.0;
        double cpuTotal = 0.0;
        double cpuMaximum = 0.0;
        double gpuTotal = 0.0;
        double gpuMaximum = 0.0;
        int gpuSampleCount = 0;
        double updateTotal = 0.0;
        double layoutTotal = 0.0;
        long layoutPasses = 0;
        long allocatedBytes = 0;
        long drawnElements = 0;
        long drawOperations = 0;
        double drawTotal = 0.0;
        double submitTotal = 0.0;
        int oldestIndex = (_nextSample - _sampleCount + MaximumSamples) % MaximumSamples;
        for (int offset = 0; offset < _sampleCount; offset++)
        {
            Sample sample = _samples[(oldestIndex + offset) % MaximumSamples];
            frameTotal += sample.FrameMilliseconds;
            frameMaximum = Math.Max(frameMaximum, sample.FrameMilliseconds);
            cpuTotal += sample.CpuMilliseconds;
            cpuMaximum = Math.Max(cpuMaximum, sample.CpuMilliseconds);
            updateTotal += sample.UpdateMilliseconds;
            layoutTotal += sample.LayoutMilliseconds;
            layoutPasses += sample.LayoutPasses;
            allocatedBytes += sample.AllocatedBytes;
            drawnElements += sample.DrawnElements;
            drawOperations += sample.DrawOperations;
            drawTotal += sample.DrawMilliseconds;
            submitTotal += sample.SubmitMilliseconds;
            if (sample.GpuMilliseconds is { } gpuMilliseconds)
            {
                gpuTotal += gpuMilliseconds;
                gpuMaximum = Math.Max(gpuMaximum, gpuMilliseconds);
                gpuSampleCount++;
            }
        }

        double averageFrame = frameTotal / _sampleCount;
        double elapsedSeconds = _sampleCount * averageFrame / 1000.0;
        double averageCpu = cpuTotal / _sampleCount;
        double averageUpdate = updateTotal / _sampleCount;
        double averageLayout = layoutTotal / _sampleCount;
        double averageDraw = drawTotal / _sampleCount;
        double averageSubmit = submitTotal / _sampleCount;
        return new PerformanceSnapshot(
            _sampleCount,
            1000.0 / averageFrame,
            averageFrame,
            frameMaximum,
            averageCpu,
            cpuMaximum,
            gpuSampleCount > 0 ? gpuTotal / gpuSampleCount : null,
            gpuSampleCount > 0 ? gpuMaximum : null,
            averageUpdate,
            averageLayout,
            averageDraw,
            elapsedSeconds > 0.0 ? layoutPasses / elapsedSeconds : 0.0,
            elapsedSeconds > 0.0 ? allocatedBytes / elapsedSeconds : 0.0,
            (double)drawnElements / _sampleCount,
            (double)drawOperations / _sampleCount,
            averageSubmit,
            // Everything the measured phases did not account for, shown rather than folded into
            // one of them, so a phase can never quietly absorb work that is not its own.
            Math.Max(0.0, averageCpu - averageUpdate - averageLayout - averageDraw - averageSubmit),
            _videoMemory,
            _memory);
    }

    private void ClearSamples()
    {
        _nextSample = 0;
        _sampleCount = 0;
    }

    private readonly record struct Sample(
        long FrameStartedTimestamp,
        double FrameMilliseconds,
        double CpuMilliseconds,
        double? GpuMilliseconds,
        double UpdateMilliseconds,
        double LayoutMilliseconds,
        int LayoutPasses,
        long AllocatedBytes,
        int DrawnElements,
        int DrawOperations,
        double DrawMilliseconds,
        double SubmitMilliseconds);
}
