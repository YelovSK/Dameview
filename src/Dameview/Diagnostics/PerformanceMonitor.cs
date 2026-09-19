using System.Diagnostics;

namespace Dameview.Diagnostics;

internal readonly record struct PerformanceFrameTiming(
    long FrameStartedTimestamp,
    TimeSpan CpuTime,
    TimeSpan? GpuTime);

internal readonly record struct PerformanceSnapshot(
    int SampleCount,
    double FramesPerSecond,
    double AverageFrameMilliseconds,
    double MaximumFrameMilliseconds,
    double AverageCpuMilliseconds,
    double MaximumCpuMilliseconds,
    double? AverageGpuMilliseconds,
    double? MaximumGpuMilliseconds);

internal sealed class PerformanceMonitor
{
    private const int MaximumSamples = 2048;
    private static readonly long SampleWindowTicks = (long)(Stopwatch.Frequency * 1.0);
    private static readonly long IdleResetTicks = (long)(Stopwatch.Frequency * 0.25);

    private readonly Sample[] _samples = new Sample[MaximumSamples];
    private int _nextSample;
    private int _sampleCount;
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
                    timing.GpuTime?.TotalMilliseconds));
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
        int oldestIndex = (_nextSample - _sampleCount + MaximumSamples) % MaximumSamples;
        for (int offset = 0; offset < _sampleCount; offset++)
        {
            Sample sample = _samples[(oldestIndex + offset) % MaximumSamples];
            frameTotal += sample.FrameMilliseconds;
            frameMaximum = Math.Max(frameMaximum, sample.FrameMilliseconds);
            cpuTotal += sample.CpuMilliseconds;
            cpuMaximum = Math.Max(cpuMaximum, sample.CpuMilliseconds);
            if (sample.GpuMilliseconds is { } gpuMilliseconds)
            {
                gpuTotal += gpuMilliseconds;
                gpuMaximum = Math.Max(gpuMaximum, gpuMilliseconds);
                gpuSampleCount++;
            }
        }

        double averageFrame = frameTotal / _sampleCount;
        return new PerformanceSnapshot(
            _sampleCount,
            1000.0 / averageFrame,
            averageFrame,
            frameMaximum,
            cpuTotal / _sampleCount,
            cpuMaximum,
            gpuSampleCount > 0 ? gpuTotal / gpuSampleCount : null,
            gpuSampleCount > 0 ? gpuMaximum : null);
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
        double? GpuMilliseconds);
}
