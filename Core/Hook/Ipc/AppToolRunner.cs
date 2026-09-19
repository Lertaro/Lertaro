using System.Diagnostics;
using Lertaro.Core.Wire;

namespace Lertaro.Core.Hook.Ipc;

/// <summary>
/// Runs a tool on the Hook's behalf, at the App's own privilege level.
/// </summary>
/// <remarks>
/// The Hook is started elevated and an elevated Directory Opus tool can never be answered by the
/// unelevated Opus -- User Interface Privilege Isolation blocks the reply, so the tool hangs and writes
/// nothing -- while starting a process at a lower integrity level needs CreateProcessAsUser and a
/// privilege the Hook does not hold. The App is already at the user's level, so it runs the tool.
/// </remarks>
internal static class AppToolRunner
{
    // Comfortably above the ~50ms a healthy run takes, and above the Hook's own wait, so the Hook is the
    // side that gives up first and this never leaves a process behind on a slow machine.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static void Run(string outputFile, string toolPath, Action<IpcMessage> respond)
    {
        var started = false;
        var processId = 0;
        var failure = string.Empty;

        try
        {
            if (string.IsNullOrEmpty(outputFile) || string.IsNullOrEmpty(toolPath))
            {
                failure = "missing output path or tool path";
            }
            else if (!File.Exists(toolPath))
            {
                failure = $"'{toolPath}' does not exist";
            }
            else
            {
                using var process = Process.Start(new ProcessStartInfo(toolPath)
                {
                    // The path is quoted: it may contain spaces, and an unquoted one would be split into
                    // separate arguments by the command-line parser.
                    Arguments = $"/info \"{outputFile}\",paths",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    failure = "Process.Start returned nothing";
                }
                else
                {
                    processId = process.Id;
                    started = process.WaitForExit((int)Timeout.TotalMilliseconds);
                    if (!started)
                    {
                        failure = $"did not exit within {Timeout.TotalSeconds:0}s";
                        try { process.Kill(); } catch { /* best effort */ }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            failure = $"{ex.GetType().Name}: {ex.Message}";
        }

        try
        {
            respond(new IpcMessage
            {
                Id = IpcMessageId.ToolResult,
                StringVal1 = outputFile,
                BoolVal = started,
                IntVal = processId
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"[AppToolRunner] could not report the tool result: {ex.Message}", LogLevel.Warn);
            return;
        }

        if (!started)
            Logger.Log($"[AppToolRunner] running '{toolPath}' failed: {failure}", LogLevel.Debug);
    }
}
