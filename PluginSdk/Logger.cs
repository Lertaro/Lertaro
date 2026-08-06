namespace Lertaro.PluginSdk;

public enum LogLevel
{
    Error = 0,
    Warn = 1,
    Info = 2,
    Debug = 3
}

public static class Logger
{
    public static Action<string, LogLevel>? LogAction { get; set; }

    public static void Log(string message, LogLevel level = LogLevel.Info) => LogAction?.Invoke(message, level);
}
