using System.IO.Pipes;
using Lertaro.Core;
using Lertaro.Core.Services.Pipe;

namespace Lertaro.App.Services.Pipe;

// Split from AppSearchPipeService solely to keep it within the repository's per-file line limit: this
// holds the accept loop and the resource limits that bound it, leaving AppSearchPipeService with the
// request handling and the searches themselves.
internal static class AppSearchPipeListener
{
    // Two independent layers, matching how AppPipeService's own activation pipe scopes itself, plus one
    // more: the per-SID and per-session suffix means a different Windows account's App instance never contends for
    // the exact same pipe name in the first place (Windows named pipes live in the machine-wide \\.\pipe\
    // namespace, not session-isolated by default), and the ACL below backs that with actual enforcement --
    // the OS itself rejects a connection attempt from any SID but the current user's, so even a guessed/
    // predicted name (Windows usernames aren't secret) can't cross accounts. This matters specifically for
    // this pipe (unlike the plain activation one) because a search request can return another user's own
    // file paths/network-drive contents.
    private static readonly string PipeName = AppPipeNames.SearchPipeName;
    private static bool _keepRunning = true;

    // A local client can open connections as fast as it likes, and each accepted one runs a handler that
    // may execute a full search. The cap is deliberately generous -- the CLI uses one connection and sends
    // its request immediately -- but it is the difference between "a client spamming connections slows
    // the App down" and "a client spamming connections exhausts it".
    internal const int MaxConcurrentClients = 16;
    internal static readonly ConnectionSlotGate ClientSlots = new(MaxConcurrentClients);

    // A client that connects and then sends nothing (or half a header) would otherwise park a handler --
    // and its slot -- until the process exits, because the read has no natural end. Same for one that
    // stops mid-request. The limit applies between requests too: this pipe serves request/response pairs,
    // so an idle connection has nothing to wait for.
    internal static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(30);

    internal static void Stop() => _keepRunning = false;

    internal static async Task ListenLoopAsync(Func<NamedPipeServerStream, Task> handleClient)
    {
        // PipeSecurityFactory.CreateCurrentUserOnly's ACL (SID-based), not the simpler
        // PipeOptions.CurrentUserOnly flag: this pipe needs to be reachable from an ELEVATED client too
        // (`lff` run from an admin terminal), and PipeOptions.CurrentUserOnly's own client-side check
        // compares token OWNER, not the actual user SID -- for a member of Administrators that's
        // BUILTIN\Administrators on both the standard and elevated token, not this (non-elevated) App's
        // own user SID, so an elevated client fails that check even though it's the very same logged-in
        // user. See CreateCurrentUserOnly's own comment for the full explanation.
        var pipeSecurity = PipeSecurityFactory.CreateCurrentUserOnly();
        if (pipeSecurity == null)
        {
            // No PipeOptions.CurrentUserOnly fallback here (unlike an earlier version of this method) --
            // that flag is precisely the buggy mechanism the ACL above replaced (see the comment on
            // CreateCurrentUserOnly), so silently falling back to it would quietly reintroduce the exact
            // "elevated client rejected" bug this exists to avoid, in whatever rare case
            // WindowsIdentity.GetCurrent().User itself fails to resolve. Unlike HookIpcServer's own
            // fallback (a plain, unrestricted pipe), this one also isn't an acceptable substitute here:
            // this pipe's results can carry another user's own file paths/network-drive contents (see the
            // PipeName comment above), so a broadened ACL is a real exposure, not just a shrug-worthy
            // degradation. Refusing to start is the honest failure mode.
            Logger.Log("[AppSearchPipeService] Could not resolve the current user's SID -- refusing to start (would otherwise need to either reintroduce a known bug or broaden this pipe's ACL, neither acceptable).", LogLevel.Error);
            return;
        }

        while (_keepRunning)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    4096, 4096,
                    pipeSecurity);

                await pipe.WaitForConnectionAsync().ConfigureAwait(false);

                if (!ClientSlots.TryAcquire())
                {
                    Logger.Log($"[AppSearchPipeService] Refused a connection: {MaxConcurrentClients} clients are already connected.", LogLevel.Warn);
                    pipe.Dispose();
                    pipe = null;
                    continue;
                }

                // Handed to the task before the try block can dispose it, so the slot is released exactly
                // once and the pipe is owned by exactly one party.
                var connectedPipe = pipe;
                pipe = null;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await handleClient(connectedPipe).ConfigureAwait(false);
                    }
                    finally
                    {
                        ClientSlots.Release();
                    }
                });
            }
            catch (Exception ex)
            {
                pipe?.Dispose();
                Logger.Log($"[AppSearchPipeService] Server connection failed: {ex.Message}", LogLevel.Error);
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
    }
}
