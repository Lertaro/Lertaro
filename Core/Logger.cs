using Lertaro.Core.Services.Installation;

namespace Lertaro.Core;

public enum LogLevel
{
    Error = 0,
    Warn = 1,
    Info = 2,
    Debug = 3
}

public static class Logger
{
    private static readonly InstallationMode CurrentInstallationMode = InstallationDetector.Detect();

    /// <summary>
    /// System-wide shared data directory: %ProgramData%\Lertaro for an installed copy, or Data\Machine
    /// beside a portable copy. A portable copy without Data reuses existing installed data for compatibility.
    /// Used by the service for logs, index cache, etc.
    /// </summary>
    public static readonly string SharedDataDir = DataDirectoryResolver.ResolveShared(
        CurrentInstallationMode,
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

    /// <summary>
    /// Per-user data directory. A verified portable copy keeps its data under Data\Users\&lt;SID hash&gt;
    /// so settings, history, certificates, and per-user caches travel with it without exposing the
    /// account SID in a path. Without Data it reuses existing %LocalAppData%\Lertaro data for compatibility.
    /// </summary>
    public static readonly string UserDataDir = DataDirectoryResolver.ResolveUser(
        CurrentInstallationMode,
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        CurrentUserIdentity.SidHash);

    private static string _logDir = string.Empty;
    private static string _logPath = string.Empty;
    private static LogLevel _minimumLevel = LogLevel.Info;
    private static readonly object LogLock = new();

    // Writing the same message over and over (a watcher loop re-failing the same file, a
    // retry storm) drowns everything else in the log. An identical consecutive message is
    // written once, condensed into a "(repeated x N)" tally line at every 10th occurrence,
    // and flushed with its final tally when a different message arrives.
    private const int RepeatReportInterval = 10;
    private const long RollOverSizeBytes = 1024 * 1024;
    private static string? _lastMessage;
    private static LogLevel _lastLevel;
    private static int _repeatsSinceFirst;

    // One long-lived handle instead of a CreateFile/write/CloseHandle cycle per line. Logger.Log is
    // called from the indexer's per-drive and per-file paths, the USN monitor loops, the pipe
    // dispatchers and the search pipeline, all of them serialized on LogLock, so at LogLevel.Debug a
    // busy index pass used to pay thousands of open/close pairs and block every other thread behind
    // each one.
    //
    // Holding that handle changes who may open the file: a reader must request FileShare.ReadWrite,
    // because File.ReadAllText/ReadLines ask for FileShare.Read, which contradicts the write access this
    // handle already holds and fails with a sharing violation. ReadLogLines below is the one place that
    // knows this; anything reading a log goes through it.
    private static StreamWriter? _writer;

    /// <summary>
    /// Reads a log file while a writer holds it open. See <see cref="_writer"/>: the BCL's convenience
    /// readers ask for a share mode that an open write handle contradicts.
    /// </summary>
    public static IReadOnlyList<string> ReadLogLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        return lines;
    }

    /// <summary>
    /// Whether the current log file exists and is still under the size cap that forces a fresh file.
    /// </summary>
    private static bool IsLogBelowRollOver() =>
        File.Exists(_logPath) && new FileInfo(_logPath).Length < RollOverSizeBytes;

    /// <summary>
    /// Opens the shared writer over <paramref name="append"/> semantics, replacing any writer from an
    /// earlier <see cref="Initialize"/>. Caller holds <see cref="LogLock"/>; the new writer is also
    /// returned so a caller can write its banner without re-checking the field.
    /// </summary>
    private static StreamWriter OpenWriter(bool append)
    {
        CloseWriter();
        // Deliberately not FileShare.Delete: a handle held for the process lifetime cannot also let the
        // file be deleted under it, and the alternative (share the delete, keep writing into a
        // deleted-but-open file) loses the rest of the run's log silently. Deleting by hand while a
        // process is running reports "file in use"; the in-app clear goes through ClearCurrentLog, which
        // owns the handle and works.
        _writer = new StreamWriter(new FileStream(_logPath, append ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
        return _writer;
    }

    private static void CloseWriter()
    {
        var writer = _writer;
        _writer = null;
        if (writer is null)
            return;

        try
        {
            writer.Dispose(); // flushes; a handle that can no longer be written must not break the caller
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Gets the directory where the current log file is stored.
    /// </summary>
    public static string LogDir => _logDir;

    public static LogLevel MinimumLevel
    {
        get => _minimumLevel;
        set => _minimumLevel = value;
    }

    /// <summary>
    /// Initialize the logger.
    /// </summary>
    /// <param name="logFileName">Log file name, e.g. "lertaro_service_log.txt"</param>
    /// <param name="baseDirectory">
    /// Base directory for the log file. Pass <see cref="SharedDataDir"/> for
    /// system-wide (service) logs, or <see cref="UserDataDir"/> for per-user (UI) logs.
    /// If null, defaults to <see cref="UserDataDir"/>.
    /// </param>
    /// <param name="overwrite">
    /// When <c>true</c>, the log file is truncated and this launch starts a fresh log. When <c>false</c>,
    /// an existing log is appended to, but only while it is under <see cref="RollOverSizeBytes"/>; a log
    /// that has already grown past that cap is truncated even then, so no single file grows without bound.
    /// </param>
    public static void Initialize(string logFileName, string? baseDirectory = null, bool overwrite = true)
    {
        lock (LogLock)
        {
            try
            {
                _logDir = Path.Combine(baseDirectory ?? UserDataDir, "logs");
                Directory.CreateDirectory(_logDir);
                _logPath = Path.Combine(_logDir, logFileName);
                _lastMessage = null;
                _repeatsSinceFirst = 0;

                var shouldAppend = !overwrite && IsLogBelowRollOver();

                OpenWriter(shouldAppend).Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] " +
                    $"{(shouldAppend ? "Log resumed" : "Log initialized")} ({logFileName})\n");
            }
            catch
            {
                // Fallback: try writing next to the executable
                CloseWriter();
                _logDir = AppDomain.CurrentDomain.BaseDirectory;
                _logPath = Path.Combine(_logDir, logFileName);
            }
        }
    }

    /// <summary>
    /// Whether a message at <paramref name="level"/> would be written. <see cref="Log"/> drops it either
    /// way; this is for callers on hot paths that would otherwise build the message string first -- an
    /// interpolation or string.Format at the call site runs even when the message is about to be discarded.
    /// </summary>
    public static bool IsEnabled(LogLevel level) => level <= _minimumLevel;

    public static void Log(string message, LogLevel level = LogLevel.Info)
    {
        if (level > _minimumLevel)
            return;

        lock (LogLock)
        {
            try
            {
                if (level == _lastLevel && message == _lastMessage)
                {
                    _repeatsSinceFirst++;
                    if ((_repeatsSinceFirst + 1) % RepeatReportInterval == 0)
                    {
                        WriteLine($"{message} (repeated x{_repeatsSinceFirst + 1})", level);
                    }
                    return;
                }

                FlushRepeatTally();
                _lastMessage = message;
                _lastLevel = level;
                _repeatsSinceFirst = 0;
                WriteLine(message, level);
            }
            catch
            {
                // Ignore
            }
        }
    }

    /// <summary>
    /// Writes the final tally of a repeat run that ended between the every-10th report
    /// points, so a consumer can tell exactly how many times the message occurred.
    /// </summary>
    private static void FlushRepeatTally()
    {
        var total = _repeatsSinceFirst + 1;
        if (_lastMessage != null && total >= 2 && total % RepeatReportInterval != 0)
        {
            WriteLine($"{_lastMessage} (repeated x{total})", _lastLevel);
        }
    }

    private static void WriteLine(string content, LogLevel level)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {content}\n";
        var writer = _writer;
        if (writer is null)
        {
            // No writer (never initialized, or the log directory was unusable and Initialize fell back to
            // the executable directory): behave exactly as before -- one open/close per line.
            File.AppendAllText(_logPath, line);
            return;
        }

        if (writer.BaseStream.Length >= RollOverSizeBytes)
        {
            // The cap used to be looked at only at init, so one long service run grew the file without
            // bound. It is checked per line now; a roll-over truncates, matching what a restart did before.
            writer = OpenWriter(append: false);
            writer.Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Log rolled over ({Path.GetFileName(_logPath)})\n");
        }

        try
        {
            writer.Write(line);
        }
        catch (IOException)
        {
            // A log that has gone unwritable (the file was deleted underneath us, the disk is full) must
            // not stay dead: drop the handle and let the next line take the per-line path.
            CloseWriter();
            File.AppendAllText(_logPath, line);
        }
    }

    /// <summary>
    /// Truncates the current process's own log file. Only the process that owns a given log file is
    /// guaranteed permission to write it -- e.g. service.log lives under the shared (ProgramData)
    /// directory the service runs with elevated/system rights over, which the App process cannot
    /// write to directly, so clearing it must be requested of the owning process via IPC instead.
    /// </summary>
    public static void ClearCurrentLog()
    {
        lock (LogLock)
        {
            try
            {
                // Through the writer, by way of recreating it: File.WriteAllText here would collide with
                // the handle WriteLine keeps open.
                OpenWriter(append: false).Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Log cleared\n");
                _lastMessage = null;
                _repeatsSinceFirst = 0;
            }
            catch
            {
                // Ignore
            }
        }
    }
}
