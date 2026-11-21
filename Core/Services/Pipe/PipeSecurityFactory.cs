using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Lertaro.Core.Services.Pipe;

// Builds pipe ACLs shared by Core and App. CreateCurrentUserOnly supports same-user connections across
// elevation levels.
public static class PipeSecurityFactory
{
    // LertaroPipe's own pipe (UsnServicePipeServer): deliberately broad. That pipe is hosted by the
    // --service process, which runs as LocalSystem (a genuine Windows Service, not a per-user process --
    // see Service\Program.cs's "--service" branch), and is meant to be reachable by EVERY locally logged
    // -in user. Restricting this ACL to one SID would restrict it to LocalSystem's own SID (since that's
    // who creates the pipe), which no real user account would ever match -- breaking local search for
    // everyone, not narrowing who can reach it.
    //
    // Reaching the pipe is not the same as seeing everything the service indexed, though: the index holds
    // every user's files, and the pipe server identifies each caller by impersonating it and filters what
    // it returns (see CallerVisibility). An elevated administrator sees everything; anyone else does not
    // see other users' profile folders.
    public static PipeSecurity? Create()
    {
        try
        {
            var pipeSecurity = new PipeSecurity();
            pipeSecurity.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl, AccessControlType.Deny));

            // Creating an instance of a named pipe that already exists is access-checked against THIS
            // DACL, and the server creates instances while previous ones are still live (UsnServicePipeServer
            // runs two overlapping listener loops with MaxAllowedServerInstances). So the right to create
            // instances belongs to the accounts that host the server -- LocalSystem, plus Administrators
            // for an interactive run -- and not to the public. Granting it to Everyone/Authenticated Users
            // let any local process add its own "LertaroPipe" instance and have the App's or CLI's requests
            // answered by it: they would receive attacker-chosen search results, file metadata and recent
            // files to display and act on, and each squatted instance reserves kernel nonpaged pool.
            foreach (var hostSid in new[]
            {
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            })
            {
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    hostSid,
                    PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                    AccessControlType.Allow
                ));
            }

            var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

            pipeSecurity.AddAccessRule(new PipeAccessRule(
                everyoneSid,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow
            ));

            var authenticatedUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

            pipeSecurity.AddAccessRule(new PipeAccessRule(
                authenticatedUsersSid,
                PipeAccessRights.ReadWrite,
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

    // Allow this logged-in user's SID to cross elevation boundaries on Hook and App search pipes.
    // Use WindowsIdentity.User for the ACL; elevation changes integrity and groups, not that SID.
    //
    // Do not use PipeOptions.CurrentUserOnly on either end: its owner check uses WindowsIdentity.Owner,
    // which can be BUILTIN\Administrators rather than the user's SID and reject a permitted connection.
    // The SID ACL enforces access. User/session components in the pipe name are not an access boundary.
    public static PipeSecurity? CreateCurrentUserOnly()
    {
        try
        {
            var pipeSecurity = new PipeSecurity();
            pipeSecurity.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl, AccessControlType.Deny));
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
