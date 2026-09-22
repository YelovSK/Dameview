using System.Diagnostics;
using System.Globalization;
using Dameview.Win32;

namespace Dameview.Diagnostics;

/// <summary>
/// Times startup stages from process creation, so the time before managed code runs counts too.
/// Window thread only.
/// </summary>
internal static class StartupTrace
{
    private const int Capacity = 32;

    private static readonly string[] _stages = new string[Capacity];
    private static readonly long[] _timestamps = new long[Capacity];
    private static long _processCreated;
    private static int _count;
    private static bool _reported;

    /// <summary>Establishes the process-creation baseline; call first in <c>Main</c>.</summary>
    internal static void Begin()
    {
        long now = Stopwatch.GetTimestamp();
        TimeSpan sinceCreation = DateTime.UtcNow - NativeMethods.GetProcessCreationTimeUtc();
        _processCreated = now - (long)(sinceCreation.TotalSeconds * Stopwatch.Frequency);
    }

    internal static void Mark(string stage)
    {
        if (_reported || _count == Capacity)
        {
            return;
        }

        _stages[_count] = stage;
        _timestamps[_count] = Stopwatch.GetTimestamp();
        _count++;
    }

    /// <summary>Writes the recorded stages and stops recording; startup ends here.</summary>
    internal static void Report()
    {
        if (_reported)
        {
            return;
        }

        _reported = true;
        long previous = _processCreated;
        for (int i = 0; i < _count; i++)
        {
            double stage = Stopwatch.GetElapsedTime(previous, _timestamps[i]).TotalMilliseconds;
            double total = Stopwatch.GetElapsedTime(_processCreated, _timestamps[i]).TotalMilliseconds;
            Log.Debug("Startup", string.Create(
                CultureInfo.InvariantCulture,
                $"{_stages[i]} +{stage:F1} = {total:F1} ms"));
            previous = _timestamps[i];
        }
    }
}
