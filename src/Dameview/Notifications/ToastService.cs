namespace Dameview.Notifications;

internal enum ToastSeverity
{
    Success,
    Warning,
    Error,
}

internal sealed record Toast(long Id, string Message, ToastSeverity Severity, long ExpiresAt);

/// <summary>
/// Holds the messages worth interrupting someone for, until they expire or are dismissed.
/// </summary>
/// <remarks>
/// Only a failed action the user asked for belongs here. Everything a subsystem merely wants
/// to record goes to the log, which nothing has to read to keep working.
/// </remarks>
internal sealed class ToastService
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

    // Older messages leave rather than pushing the stack past what fits on screen.
    private const int Capacity = 4;

    private readonly TimeProvider _time;
    private readonly List<Toast> _toasts = [];
    private long _nextId;

    internal ToastService(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    internal event Action? Changed;

    internal IReadOnlyList<Toast> Toasts => _toasts;

    internal void Notify(string message, ToastSeverity severity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        long expiresAt = _time.GetTimestamp()
            + (long)(Lifetime.TotalSeconds * _time.TimestampFrequency);
        _toasts.Add(new Toast(_nextId++, message, severity, expiresAt));
        if (_toasts.Count > Capacity)
        {
            _toasts.RemoveRange(0, _toasts.Count - Capacity);
        }

        Changed?.Invoke();
    }

    internal void Dismiss(long id)
    {
        if (_toasts.RemoveAll(toast => toast.Id == id) > 0)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Drops the messages whose time is up.</summary>
    /// <returns><see langword="true"/> when anything was removed.</returns>
    internal bool RemoveExpired()
    {
        // A monotonic timestamp, so that the clock moving does not cut a message short.
        long now = _time.GetTimestamp();
        if (_toasts.RemoveAll(toast => toast.ExpiresAt <= now) == 0)
        {
            return false;
        }

        Changed?.Invoke();
        return true;
    }
}
