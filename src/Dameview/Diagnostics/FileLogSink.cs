using System.Threading.Channels;

namespace Dameview.Diagnostics;

internal sealed class FileLogSink : IDisposable
{
    private const int RetentionDays = 7;
    private readonly Channel<string> _entries = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly Task _writer;
    private readonly string _directory;
    private readonly string _filePrefix;
    private readonly string _fileExtension;
    private readonly Mutex _writeMutex = new(initiallyOwned: false, "Local\\DameviewLog");
    private int _disposed;

    internal FileLogSink(string path)
    {
        string fullPath = Path.GetFullPath(path);
        _directory = Path.GetDirectoryName(fullPath)!;
        _filePrefix = Path.GetFileNameWithoutExtension(fullPath);
        _fileExtension = Path.GetExtension(fullPath);
        Directory.CreateDirectory(_directory);
        DeleteExpiredFiles(DateTime.Today);

        _writer = Task.Run(WriteEntries);
    }

    internal void Write(string entry)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            _entries.Writer.TryWrite(entry);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _entries.Writer.TryComplete();
        try
        {
            _writer.GetAwaiter().GetResult();
        }
        catch
        {
            // A logging failure must not escape during application shutdown.
        }

        _writeMutex.Dispose();
    }

    private async Task WriteEntries()
    {
        StreamWriter? writer = null;
        DateTime currentDate = default;
        var pendingEntries = new List<string>();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        Task<bool> readTask = _entries.Reader.WaitToReadAsync().AsTask();
        Task<bool> flushTask = timer.WaitForNextTickAsync().AsTask();
        try
        {
            while (true)
            {
                await Task.WhenAny(readTask, flushTask);
                if (flushTask.IsCompleted)
                {
                    WriteBatch(pendingEntries, ref writer, ref currentDate);
                    pendingEntries.Clear();
                    flushTask = timer.WaitForNextTickAsync().AsTask();
                }
                else
                {
                    if (!await readTask)
                    {
                        WriteBatch(pendingEntries, ref writer, ref currentDate);
                        break;
                    }

                    while (_entries.Reader.TryRead(out string? entry))
                    {
                        pendingEntries.Add(entry);
                    }

                    readTask = _entries.Reader.WaitToReadAsync().AsTask();
                }
            }
        }
        catch
        {
            // A logging failure must not affect the application.
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private void WriteBatch(List<string> entries, ref StreamWriter? writer, ref DateTime currentDate)
    {
        if (entries.Count == 0)
        {
            return;
        }

        EnterWriteMutex();
        try
        {
            DateTime date = DateTime.Today;
            if (writer is null || date != currentDate)
            {
                writer?.Dispose();
                string dailyPath = GetDailyPath(date);
                writer = new StreamWriter(
                    new FileStream(dailyPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                currentDate = date;
                DeleteExpiredFiles(date);
            }

            writer.Flush();
            writer.BaseStream.Seek(0, SeekOrigin.End);
            foreach (string entry in entries)
            {
                date = DateTime.Today;
                if (date != currentDate)
                {
                    writer.Flush();
                    writer.Dispose();
                    string dailyPath = GetDailyPath(date);
                    writer = new StreamWriter(
                        new FileStream(dailyPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                        new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    currentDate = date;
                    DeleteExpiredFiles(date);
                    writer.Flush();
                    writer.BaseStream.Seek(0, SeekOrigin.End);
                }

                writer.WriteLine(entry);
            }

            writer.Flush();
        }
        finally
        {
            _writeMutex.ReleaseMutex();
        }
    }

    private void EnterWriteMutex()
    {
        try
        {
            _writeMutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // Ownership was transferred to this process.
        }
    }

    private string GetDailyPath(DateTime date) => Path.Combine(
        _directory,
        $"{_filePrefix}-{date:yyyy-MM-dd}{_fileExtension}");

    private void DeleteExpiredFiles(DateTime currentDate)
    {
        DateTime firstRetainedDate = currentDate.Date.AddDays(-(RetentionDays - 1));
        string searchPattern = $"{_filePrefix}-*{_fileExtension}";
        try
        {
            foreach (string path in Directory.EnumerateFiles(_directory, searchPattern))
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                string dateText = fileName[(_filePrefix.Length + 1)..];
                if (DateTime.TryParseExact(
                        dateText,
                        "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out DateTime fileDate)
                    && fileDate.Date < firstRetainedDate)
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                        // An old log that is locked or inaccessible can remain until the next cleanup.
                    }
                }
            }
        }
        catch
        {
            // Log cleanup must not affect logging or application startup.
        }
    }
}
