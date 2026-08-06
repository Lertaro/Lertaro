using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Lertaro.Core.Services.Pipe;

// Split out of UsnServicePipeServer to keep that file under the line-count limit. Public (not internal)
// so App\Services\AppSearchPipeService.cs -- a different assembly -- can reuse CreateCurrentUserOnly
// too, for the same elevation-crossing reason HookIpcServer needed it (see that method's own comment).
public static class PipeSecurityFactory
{
    // LertaroPipe's own pipe (UsnServicePipeServer): deliberately broad. That pipe is hosted by the
    // --service process, which runs as LocalSystem (a genuine Windows Service, not a per-user process --
    // see Service\Program.cs's "--service" branch), and is meant to be reachable by EVERY locally logged
    // -in user asking about the same local filesystem -- local file existence isn't account-scoped data
    // the way network-drive contents or search history are, so any authenticated user on this machine is
    // already entitled to see it via Explorer regardless. Restricting this ACL to one SID would restrict
    // it to LocalSystem's own SID (since that's who creates the pipe), which no real user account would
    // ever match -- breaking local search for everyone, not narrowing who can reach it.
    public static PipeSecurity? Create()
    {
        try
        {
            var pipeSecurity = new PipeSecurity();
            var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

            pipeSecurity.AddAccessRule(new PipeAccessRule(
                everyoneSid,
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow
            ));

            var authenticatedUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

            pipeSecurity.AddAccessRule(new PipeAccessRule(
                authenticatedUsersSid,
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow
            ));
            Logger.Log("[PipeServer] PipeSecurity successfully configured.", LogLevel.Debug);
            return pipeSecurity;
        }
        catch (Exception ex)
        {
            Logger.Log($"[PipeServer] Failed to create PipeSecurity: {ex.Message}", LogLevel.Error);
            return null;
        }
    }

    // For pipes that need to cross an elevation boundary for the SAME logged-in user -- originally the
    // Hook process's two pipes (HookIpcServer: server runs elevated, client is the App at standard
    // integrity), and also AppSearchPipeService's search pipe (server is the App at standard integrity,
    // but a client run from an elevated/"Run as administrator" terminal needs to reach it too). Scopes
    // the ACL to just the actual user's own SID, not every authenticated user on the machine.
    //
    // NamedPipeServerStreamAcl.Create with THIS ACL -- not the simpler PipeOptions.CurrentUserOnly flag on
    // both ends -- is required specifically because of a subtlety in what "current user" means for a
    // Windows token: WindowsIdentity.GetCurrent().User (used below) always reflects the actual logged-in
    // user's SID regardless of elevation -- UAC's split-token model changes a token's integrity level and
    // group memberships, not the identity it represents. But .NET's own PipeOptions.CurrentUserOnly
    // doesn't check that; it compares against WindowsIdentity.GetCurrent().Owner, and for a user who's a
    // member of Administrators, Windows sets the TOKEN OWNER to BUILTIN\Administrators on BOTH the
    // filtered and elevated tokens -- NOT the user's own SID. A non-elevated server created with
    // PipeOptions.CurrentUserOnly is fine for a non-elevated client (same Owner), but an elevated client
    // (e.g. `lff` run from an admin terminal) trips the CLIENT-side CurrentUserOnly check against a
    // server whose owner doesn't match either, throwing UnauthorizedAccessException ("was not owned by
    // the current user") even though the SID-based ACL below would happily have allowed it. This is why
    // BOTH the server (this ACL) and the client (a plain NamedPipeClientStream with no
    // PipeOptions.CurrentUserOnly of its own) need to avoid that flag entirely and let the ACL alone be
    // the enforcement (the pipe NAME can still embed the user+session for obscurity, but that's not an
    // OS-enforced boundary on its own).
    public static PipeSecurity? CreateCurrentUserOnly()
    {
        try
        {
            var pipeSecurity = new PipeSecurity();
            var currentUserSid = WindowsIdentity.GetCurrent().User
                ?? throw new InvalidOperationException("Could not resolve the current process's user SID.");

            pipeSecurity.AddAccessRule(new PipeAccessRule(
                currentUserSid,
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow
            ));

            Logger.Log("[PipeServer] Current-user-only PipeSecurity successfully configured.", LogLevel.Debug);
            return pipeSecurity;
        }
        catch (Exception ex)
        {
            Logger.Log($"[PipeServer] Failed to create current-user-only PipeSecurity: {ex.Message}", LogLevel.Error);
            return null;
        }
    }
}
