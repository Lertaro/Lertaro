using System.Collections.Concurrent;
using Lertaro.Core.Wire;

namespace Lertaro.Core.Hook.Ipc;

/// <summary>
/// Runs a tool in the App process -- and therefore at the App's own privilege level -- on behalf of the
/// Hook, which cannot do it itself.
/// </summary>
/// <remarks>
/// The Hook is launched elevated (see <c>HookIpcClient.LaunchHookProcessAsync</c>), and an ELEVATED
/// Directory Opus tool can never be answered by the unelevated Opus: User Interface Privilege Isolation
/// blocks the reply, so the tool hangs and writes nothing. Measured on one machine, same command line:
/// unelevated exits in ~50ms and writes its XML, elevated never exits.
/// Starting a process at a LOWER integrity level needs <c>CreateProcessAsUser</c>, which needs
/// SeAssignPrimaryTokenPrivilege -- not present in the Hook's token -- so no amount of token juggling
/// inside the Hook can fix it. The App already runs at the user's level, so it runs the tool instead.
/// </remarks>
public static class HookToolRunRequest
{
    // One run at a time: the caller is a single background snapshot build, and a second overlapping run
    // would only ask for the same current state twice.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> Pending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The pipe back to the App, set by the Hook at startup. Null means the App is not reachable.</summary>
    public static Action<IpcMessage>? SendToApp { get; set; }

    /// <summary>
    /// Asks the App to run <paramref name="toolPath"/> with <c>/info "&lt;outputFile&gt;",paths</c> and
    /// waits for it to finish. Returns true when the App reports the tool ran to completion.
    /// </summary>
    /// <remarks>
    /// The output file is created here, empty, before the request goes out: the tool fills in a file that
    /// already exists, and creating it in the Hook is what makes it readable here afterwards. The App
    /// writes into it with its own token, so nothing has to be handed across the pipe.
    /// </remarks>
    public static async Task<bool> RunDopusRtAsync(string toolPath, string outputFile, TimeSpan timeout, CancellationToken token = default)
    {
        var send = SendToApp;
        if (send == null) return false;

        try { File.WriteAllBytes(outputFile, []); }
        catch (Exception) { return false; }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Pending.TryAdd(outputFile, completion)) return false;

        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            send(new IpcMessage { Id = IpcMessageId.RunTool, StringVal1 = outputFile, StringVal2 = toolPath });

            var finished = await Task.WhenAny(completion.Task, Task.Delay(timeout, token)).ConfigureAwait(false);
            return finished == completion.Task && completion.Task.Result;
        }
        catch (OperationCanceledException) { return false; }
        finally
        {
            Pending.TryRemove(outputFile, out _);
            Gate.Release();
        }
    }

    /// <summary>
    /// Records the App's answer, releasing whoever is waiting on that output file. Called from the Hook's
    /// IPC read loop, so it must not block: it only completes a task.
    /// </summary>
    public static void Complete(IpcMessage message)
    {
        var outputFile = message.StringVal1;
        if (string.IsNullOrEmpty(outputFile)) return;

        if (Pending.TryGetValue(outputFile, out var completion))
            completion.TrySetResult(message.BoolVal);
    }
}
