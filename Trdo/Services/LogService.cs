using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Windows.Storage;

namespace Trdo.Services;

/// <summary>
/// Lightweight, thread-safe file logger that persists diagnostics so the user
/// can retrieve them (unlike <see cref="Debug.WriteLine"/>, which only reaches an
/// attached debugger and is stripped from Release builds).
///
/// Design goals:
/// <list type="bullet">
///   <item>Never block or slow a caller. Playback events fire from hot threads
///   (UI dispatcher, WinRT media callbacks, the NAudio capture thread), so
///   <see cref="Info"/>/<see cref="Warn"/>/<see cref="Error"/> only format a line
///   and drop it into a bounded in-memory queue — all disk I/O happens on a single
///   background writer thread. If the queue is full (a burst faster than the disk),
///   lines are dropped rather than blocking the caller.</item>
///   <item>Bounded disk usage. The active file rolls to a single previous
///   generation at <see cref="MaxLogBytes"/>, so total on-disk size never exceeds
///   ~2× that. Size is tracked in memory to avoid a stat syscall per line.</item>
///   <item>Never throw into playback — every path is wrapped in try/catch.</item>
/// </list>
///
/// Writes to <c>ApplicationData.Current.LocalFolder\logs\traydio.log</c>. Follows
/// the app's convention of static services (see <see cref="SettingsService"/>).
/// </summary>
public static class LogService
{
    private const string LogFolderName = "logs";
    private const string LogFileName = "traydio.log";
    private const string PreviousLogFileName = "traydio.prev.log";

    // Roll the active file at ~1 MB; with one previous generation kept, total
    // on-disk usage is bounded to ~2 MB.
    private const long MaxLogBytes = 1 * 1024 * 1024;

    // Upper bound on queued-but-not-yet-written lines. Sized so a burst is
    // absorbed without unbounded memory; excess is dropped (and counted).
    private const int MaxQueuedLines = 8192;

    // Serializes access to the StreamWriter and size counter between the writer
    // thread and readers (ReadRecentText). Producers never take this lock.
    private static readonly object Gate = new();

    private static readonly BlockingCollection<string> Queue =
        new(new ConcurrentQueue<string>(), MaxQueuedLines);

    private static volatile bool _started;
    private static volatile bool _disabled;
    private static string? _logFolderPath;
    private static string? _logFilePath;
    private static string? _previousLogFilePath;

    private static StreamWriter? _writer;
    private static long _currentSize;
    private static int _droppedSinceLastNote;
    private static Thread? _worker;

    /// <summary>Full path to the folder that contains the log files.</summary>
    public static string LogFolderPath
    {
        get
        {
            EnsureStarted();
            return _logFolderPath ?? string.Empty;
        }
    }

    /// <summary>Full path to the current log file.</summary>
    public static string LogFilePath
    {
        get
        {
            EnsureStarted();
            return _logFilePath ?? string.Empty;
        }
    }

    public static void Info(string component, string message) => Enqueue("INFO", component, message);

    public static void Warn(string component, string message) => Enqueue("WARN", component, message);

    public static void Error(string component, string message, Exception? ex = null)
    {
        string text = ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}";
        Enqueue("ERROR", component, text);
    }

    /// <summary>
    /// Returns the tail of the log (current file, plus the previous generation when
    /// the current one is short) capped at <paramref name="maxChars"/> characters,
    /// for the "Copy diagnostics" feature. Prepended with a fresh session header.
    /// </summary>
    public static string ReadRecentText(int maxChars = 60_000)
    {
        EnsureStarted();

        var sb = new StringBuilder();
        sb.AppendLine(BuildHeader());
        sb.AppendLine();

        lock (Gate)
        {
            try
            {
                // Flush anything buffered so the copy reflects the latest lines.
                _writer?.Flush();

                string current = ReadFileSafe(_logFilePath);
                string previous = string.Empty;

                // Include the previous generation only if the current file is small,
                // so a copy captures context around a recent rollover.
                if (current.Length < maxChars / 2)
                {
                    previous = ReadFileSafe(_previousLogFilePath);
                }

                string combined = previous.Length > 0 ? previous + current : current;
                if (combined.Length > maxChars)
                {
                    combined = combined[^maxChars..];
                }

                sb.Append(combined);
            }
            catch
            {
                // Best effort — return whatever header we have.
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Reduces a stream URL to <c>scheme://host</c> (dropping path, query and any
    /// embedded credentials/tokens) so logs never leak sensitive URL contents.
    /// </summary>
    public static string Redact(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "(none)";
        }

        try
        {
            var uri = new Uri(url);
            return $"{uri.Scheme}://{uri.Host}";
        }
        catch
        {
            return "(unparsable-url)";
        }
    }

    private static void Enqueue(string level, string component, string message)
    {
        // Timestamp at the call site so ordering/latency of the writer never skews it.
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{component}] {message}";

#if DEBUG
        Debug.WriteLine(line);
#endif

        EnsureStarted();
        if (_disabled)
        {
            return;
        }

        // Non-blocking: if the writer can't keep up, drop rather than stall a
        // playback thread. Dropped lines are counted and noted in the file later.
        if (!Queue.TryAdd(line))
        {
            Interlocked.Increment(ref _droppedSinceLastNote);
        }
    }

    private static void EnsureStarted()
    {
        if (_started)
        {
            return;
        }

        lock (Gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;

            try
            {
                string root = ApplicationData.Current.LocalFolder.Path;
                _logFolderPath = Path.Combine(root, LogFolderName);
                Directory.CreateDirectory(_logFolderPath);
                _logFilePath = Path.Combine(_logFolderPath, LogFileName);
                _previousLogFilePath = Path.Combine(_logFolderPath, PreviousLogFileName);

                _worker = new Thread(WriterLoop)
                {
                    IsBackground = true,
                    Name = "LogService.Writer",
                    Priority = ThreadPriority.BelowNormal
                };
                _worker.Start();

                // Best-effort flush of buffered lines on normal process exit.
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();

                Queue.TryAdd(BuildHeader());
            }
            catch
            {
                // Couldn't set up logging — become a no-op (Debug mirror still works).
                _disabled = true;
                _logFilePath = null;
            }
        }
    }

    private static void WriterLoop()
    {
        try
        {
            foreach (string line in Queue.GetConsumingEnumerable())
            {
                lock (Gate)
                {
                    try
                    {
                        EnsureWriter();
                        if (_writer is null)
                        {
                            continue;
                        }

                        int dropped = Interlocked.Exchange(ref _droppedSinceLastNote, 0);
                        if (dropped > 0)
                        {
                            WriteLineInternal(
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [WARN] [LogService] Dropped {dropped} log line(s) (queue full)");
                        }

                        WriteLineInternal(line);

                        // Flush once the burst has drained so readers and a crash-exit
                        // see recent lines, without paying a flush on every line.
                        if (Queue.Count == 0)
                        {
                            _writer.Flush();
                        }
                    }
                    catch
                    {
                        // Drop this line; keep the loop alive.
                    }
                }
            }
        }
        catch
        {
            // GetConsumingEnumerable throws only when the collection is disposed; exit quietly.
        }
    }

    /// <summary>Writes one line and rolls the file first if it would exceed the cap.
    /// Caller must hold <see cref="Gate"/> and have a non-null <see cref="_writer"/>.</summary>
    private static void WriteLineInternal(string line)
    {
        // +2 accounts for the newline; approximate is fine for a size trigger.
        long lineBytes = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
        if (_currentSize + lineBytes > MaxLogBytes)
        {
            Roll();
        }

        _writer!.WriteLine(line);
        _currentSize += lineBytes;
    }

    /// <summary>Opens the writer if needed. Caller must hold <see cref="Gate"/>.</summary>
    private static void EnsureWriter()
    {
        if (_writer is not null || _logFilePath is null)
        {
            return;
        }

        // Append so a prior session's tail is preserved; share read so the user can
        // open the file (or ReadRecentText run) while we hold it.
        var stream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = false
        };
        _currentSize = stream.Length;
    }

    /// <summary>Rolls the active file to the previous generation and reopens a fresh
    /// one. Caller must hold <see cref="Gate"/>.</summary>
    private static void Roll()
    {
        if (_logFilePath is null || _previousLogFilePath is null)
        {
            return;
        }

        try
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;

            if (File.Exists(_previousLogFilePath))
            {
                File.Delete(_previousLogFilePath);
            }

            File.Move(_logFilePath, _previousLogFilePath);
        }
        catch
        {
            // If the roll fails, fall through and keep appending to the existing file.
        }

        try
        {
            EnsureWriter();
        }
        catch
        {
            _writer = null;
        }
    }

    private static void Shutdown()
    {
        try
        {
            Queue.CompleteAdding();
            _worker?.Join(TimeSpan.FromSeconds(2));

            lock (Gate)
            {
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
            }
        }
        catch
        {
            // Best effort on exit.
        }
    }

    private static string BuildHeader()
    {
        string version = "unknown";
        try
        {
            Windows.ApplicationModel.PackageVersion v = Windows.ApplicationModel.Package.Current.Id.Version;
            version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }
        catch
        {
            // Unpackaged or unavailable — leave "unknown".
        }

        string arch = RuntimeInformationArchitecture();
        return $"===== Traydio session started {DateTime.Now:yyyy-MM-dd HH:mm:ss} | v{version} | {Environment.OSVersion} | {arch} =====";
    }

    private static string RuntimeInformationArchitecture()
    {
        try
        {
            return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
        }
        catch
        {
            return "unknown-arch";
        }
    }

    private static string ReadFileSafe(string? path)
    {
        try
        {
            return path is not null && File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
