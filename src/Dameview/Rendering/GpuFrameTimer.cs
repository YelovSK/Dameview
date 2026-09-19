using SharpGen.Runtime;
using Vortice.Direct3D11;

namespace Dameview.Rendering;

internal sealed unsafe class GpuFrameTimer : IDisposable
{
    private const int BufferedFrameCount = 4;

    private readonly ID3D11DeviceContext _context;
    private readonly QuerySet[] _querySets;
    private int _nextQuerySet;
    private QuerySet? _activeQuerySet;

    internal GpuFrameTimer(ID3D11Device device, ID3D11DeviceContext context)
    {
        _context = context;
        _querySets = new QuerySet[BufferedFrameCount];
        for (int index = 0; index < _querySets.Length; index++)
        {
            _querySets[index] = new QuerySet(device);
        }
    }

    internal TimeSpan? BeginFrame()
    {
        TimeSpan? completed = TryCollectCompletedFrame();
        QuerySet queries = _querySets[_nextQuerySet];
        if (queries.Pending)
        {
            return completed;
        }

        _context.Begin(queries.Disjoint);
        _context.End(queries.Start);
        _activeQuerySet = queries;
        return completed;
    }

    internal void EndFrame()
    {
        if (_activeQuerySet is not { } queries)
        {
            return;
        }

        _context.End(queries.End);
        _context.End(queries.Disjoint);
        queries.Pending = true;
        _activeQuerySet = null;
        _nextQuerySet = (_nextQuerySet + 1) % _querySets.Length;
    }

    public void Dispose()
    {
        foreach (QuerySet queries in _querySets)
        {
            queries.Dispose();
        }
    }

    private TimeSpan? TryCollectCompletedFrame()
    {
        for (int offset = 0; offset < _querySets.Length; offset++)
        {
            int index = (_nextQuerySet + offset) % _querySets.Length;
            QuerySet queries = _querySets[index];
            if (!queries.Pending)
            {
                continue;
            }

            if (!TryRead(queries.Disjoint, out QueryDataTimestampDisjoint disjoint)
                || !TryRead(queries.Start, out ulong start)
                || !TryRead(queries.End, out ulong end))
            {
                continue;
            }

            queries.Pending = false;

            if (disjoint.Disjoint || disjoint.Frequency == 0 || end < start)
            {
                return null;
            }

            return TimeSpan.FromSeconds((end - start) / (double)disjoint.Frequency);
        }

        return null;
    }

    private bool TryRead<T>(ID3D11Query query, out T value) where T : unmanaged
    {
        T data = default;
        Result result = _context.GetData(query, (nint)(&data), (uint)sizeof(T),
            AsyncGetDataFlags.DoNotFlush);
        result.CheckError();
        value = data;
        return result.Code == 0; // S_FALSE is successful HRESULT, but data is not ready.
    }

    private sealed class QuerySet : IDisposable
    {
        internal QuerySet(ID3D11Device device)
        {
            Disjoint = device.CreateQuery(new QueryDescription(QueryType.TimestampDisjoint, QueryFlags.None));
            Start = device.CreateQuery(new QueryDescription(QueryType.Timestamp, QueryFlags.None));
            End = device.CreateQuery(new QueryDescription(QueryType.Timestamp, QueryFlags.None));
        }

        internal ID3D11Query Disjoint { get; }
        internal ID3D11Query Start { get; }
        internal ID3D11Query End { get; }
        internal bool Pending { get; set; }

        public void Dispose()
        {
            End.Dispose();
            Start.Dispose();
            Disjoint.Dispose();
        }
    }
}
