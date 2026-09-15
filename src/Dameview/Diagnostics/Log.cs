using System.Globalization;

namespace Dameview.Diagnostics;

public static class Log
{
    private static FileLogSink? _sink;
    private static int _minimumLevel;

    public static void Initialize(string? path = null)
    {
        if (_sink is not null)
        {
            return;
        }

        try
        {
#if DEBUG
            _minimumLevel = (int)LogLevel.Debug;
#else
            _minimumLevel = (int)LogLevel.Info;
#endif
            path ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Dameview", "Logs", "Dameview.log");
            var sink = new FileLogSink(path);
            if (Interlocked.CompareExchange(ref _sink, sink, null) is not null)
            {
                sink.Dispose();
            }
        }
        catch
        {
            // Logging must never prevent the application from starting.
        }
    }

    public static void Shutdown()
    {
        Interlocked.Exchange(ref _sink, null)?.Dispose();
    }

    public static void SetMinimumLevel(LogLevel minimumLevel)
    {
        if (Enum.IsDefined(minimumLevel))
        {
            Volatile.Write(ref _minimumLevel, (int)minimumLevel);
        }
    }

    public static void Debug(string category, string message) => Write(LogLevel.Debug, category, message, null);

    public static void Debug(string category, string message, Exception exception) =>
        Write(LogLevel.Debug, category, message, exception);

    public static void Info(string category, string message) => Write(LogLevel.Info, category, message, null);

    public static void Warning(string category, string message) => Write(LogLevel.Warning, category, message, null);

    public static void Error(string category, string message, Exception? exception = null) =>
        Write(LogLevel.Error, category, message, exception);

    private static void Write(LogLevel level, string category, string message, Exception? exception)
    {
        try
        {
            if ((int)level < Volatile.Read(ref _minimumLevel))
            {
                return;
            }

            string entry = string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] [{GetLabel(level)}] [{category}] {message}");
            if (exception is not null)
            {
                entry += Environment.NewLine + exception;
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine(entry);
#endif
            _sink?.Write(entry);
        }
        catch
        {
            // Logging must never change application behavior.
        }
    }

    private static string GetLabel(LogLevel level) => level switch
    {
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        _ => "UNKNOWN",
    };
}
