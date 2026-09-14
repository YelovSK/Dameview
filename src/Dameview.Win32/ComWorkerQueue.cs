using System.Diagnostics.CodeAnalysis;

namespace Dameview.Win32;

// Runs work on dedicated MTA threads. Each thread builds one context instance from
// <paramref name="createContext"/> (for example a decoder) and reuses it across work items.
// Callers get a Task for ordering, cancellation, and composition; this type owns only the
// thread, the priority queue, COM initialization, and context lifetime.
internal sealed class ComWorkerQueue<TContext> : IDisposable
{
    private readonly object _sync = new();
    private readonly PriorityQueue<WorkItem, (int Priority, long Sequence)> _pending = new();
    private readonly Func<TContext> _createContext;
    private readonly Thread[] _workers;
    private long _sequence;
    private bool _stopping;
    private Exception? _failure;

    internal ComWorkerQueue(string name, int workerCount, Func<TContext> createContext)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerCount);
        _createContext = createContext;

        _workers = new Thread[workerCount];
        for (int index = 0; index < workerCount; index++)
        {
            _workers[index] = new Thread(Run)
            {
                IsBackground = true,
                Name = workerCount == 1 ? name : $"{name} {index + 1}",
            };
        }

        foreach (Thread worker in _workers)
        {
            worker.Start();
        }
    }

    // Higher priority runs first; equal priorities run in FIFO order.
    internal Task<T> Enqueue<T>(
        Func<TContext, CancellationToken, T> work,
        int priority = 0,
        CancellationToken cancellationToken = default)
    {
        var item = new ResultWorkItem<T>(work, cancellationToken);
        Schedule(item, priority);
        return item.Task;
    }

    internal Task Enqueue(
        Action<TContext, CancellationToken> work,
        int priority = 0,
        CancellationToken cancellationToken = default)
    {
        var item = new ActionWorkItem(work, cancellationToken);
        Schedule(item, priority);
        return item.Task;
    }

    public void Dispose()
    {
        List<WorkItem> pending;
        lock (_sync)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            pending = DrainPending();
            Monitor.PulseAll(_sync);
        }

        foreach (WorkItem item in pending)
        {
            item.Cancel();
        }
    }

    private void Schedule(WorkItem item, int priority)
    {
        lock (_sync)
        {
            if (_failure is not null)
            {
                item.Fail(_failure);
                return;
            }

            if (_stopping)
            {
                item.Cancel();
                return;
            }

            _pending.Enqueue(item, (-priority, _sequence++));
            Monitor.Pulse(_sync);
        }
    }

    private void Run()
    {
        bool comInitialized = false;
        TContext context = default!;
        try
        {
            try
            {
                NativeMethods.InitializeComApartment(ComApartment.MultiThreaded);
                comInitialized = true;
                context = _createContext();
            }
            catch (Exception exception)
            {
                Fail(exception);
                return;
            }

            while (TryTake(out WorkItem? item))
            {
                item.Execute(context);
            }
        }
        finally
        {
            (context as IDisposable)?.Dispose();
            if (comInitialized)
            {
                NativeMethods.UninitializeComApartment();
            }
        }
    }

    private bool TryTake([NotNullWhen(true)] out WorkItem? item)
    {
        lock (_sync)
        {
            while (!_stopping)
            {
                if (_pending.TryDequeue(out WorkItem? candidate, out _))
                {
                    if (candidate.CancellationToken.IsCancellationRequested)
                    {
                        candidate.Cancel();
                        continue;
                    }

                    item = candidate;
                    return true;
                }

                Monitor.Wait(_sync);
            }

            item = null;
            return false;
        }
    }

    private void Fail(Exception exception)
    {
        List<WorkItem> pending;
        lock (_sync)
        {
            _failure = exception;
            _stopping = true;
            pending = DrainPending();
            Monitor.PulseAll(_sync);
        }

        foreach (WorkItem item in pending)
        {
            item.Fail(exception);
        }
    }

    // The caller must hold _sync.
    private List<WorkItem> DrainPending()
    {
        var items = new List<WorkItem>(_pending.Count);
        while (_pending.TryDequeue(out WorkItem? item, out _))
        {
            items.Add(item);
        }

        return items;
    }

    private abstract class WorkItem(CancellationToken cancellationToken)
    {
        internal CancellationToken CancellationToken { get; } = cancellationToken;

        internal abstract void Execute(TContext context);
        internal abstract void Cancel();
        internal abstract void Fail(Exception exception);
    }

    private sealed class ResultWorkItem<T>(
        Func<TContext, CancellationToken, T> work,
        CancellationToken cancellationToken) : WorkItem(cancellationToken)
    {
        private readonly TaskCompletionSource<T> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task<T> Task => _completion.Task;

        internal override void Execute(TContext context)
        {
            if (CancellationToken.IsCancellationRequested)
            {
                _completion.TrySetCanceled(CancellationToken);
                return;
            }

            try
            {
                _completion.TrySetResult(work(context, CancellationToken));
            }
            catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
            {
                _completion.TrySetCanceled(CancellationToken);
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }

        internal override void Cancel() => _completion.TrySetCanceled(CancellationToken);
        internal override void Fail(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class ActionWorkItem(
        Action<TContext, CancellationToken> work,
        CancellationToken cancellationToken) : WorkItem(cancellationToken)
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Task => _completion.Task;

        internal override void Execute(TContext context)
        {
            if (CancellationToken.IsCancellationRequested)
            {
                _completion.TrySetCanceled(CancellationToken);
                return;
            }

            try
            {
                work(context, CancellationToken);
                _completion.TrySetResult();
            }
            catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
            {
                _completion.TrySetCanceled(CancellationToken);
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }

        internal override void Cancel() => _completion.TrySetCanceled(CancellationToken);
        internal override void Fail(Exception exception) => _completion.TrySetException(exception);
    }
}
