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
    /// beside a portable copy. Used by the service for logs, index cache, etc.
    /// </summary>
    public static readonly string SharedDataDir = DataDirectoryResolver.ResolveShared(
        CurrentInstallationMode,
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

    /// <summary>
    /// Per-user data directory. A verified portable copy keeps its data under Data\Users\&lt;SID hash&gt;
    /// so settings, history, certificates, and per-user caches travel with it without exposing the
    /// account SID in a path. Installed copies retain %LocalAppData%\Lertaro.
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
    /// <param name="overwrite">Whether to overwrite the log file on init.</param>
    public static void Initialize(string logFileName, string? baseDirectory = null, bool overwrite = true)
    {
        lock (LogLock)
        {
            try
            {
                _logDir = Path.Combine(baseDirectory ?? UserDataDir, "logs");
                Directory.CreateDirectory(_logDir);
                _logPath = Path.Combine(_logDir, logFileName);

                var shouldAppend = false;
                if (File.Exists(_logPath))
                {
                    var fileInfo = new FileInfo(_logPath);
                    if (fileInfo.Length < 1024 * 1024)
                    {
                        shouldAppend = true;
                    }
                }

                if (shouldAppend)
                {
                    File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Log resumed ({logFileName})\n");
                }
                else
                {
                    File.WriteAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Log initialized ({logFileName})\n");
                }
            }
            catch
            {
                // Fallback: try writing next to the executable
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
                File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}\n");
            }
            catch
            {
                // Ignore
            }
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
                File.WriteAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Log cleared\n");
            }
            catch
            {
                // Ignore
            }
        }
    }
}
