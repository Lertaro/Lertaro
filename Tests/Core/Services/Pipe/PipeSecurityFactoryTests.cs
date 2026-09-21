using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Lertaro.Core.Services.Pipe;

namespace Lertaro.Core.Tests.Services.Pipe;

// Who may add an instance of the service-hosted pipe is decided entirely by the descriptor this factory
// builds, and reading the granted rights back out of it is pure -- so the boundary is pinned here instead
// of only being reviewed by eye.
[TestClass]
public sealed class PipeSecurityFactoryTests
{
    private static PipeAccessRights GrantedRights(PipeSecurity security, WellKnownSidType kind)
    {
        var sid = new SecurityIdentifier(kind, null);
        var granted = (PipeAccessRights)0;
        foreach (var rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)).OfType<PipeAccessRule>())
        {
            if (rule.AccessControlType == AccessControlType.Allow && sid.Equals(rule.IdentityReference))
                granted |= rule.PipeAccessRights;
        }

        return granted;
    }

    [TestMethod]
    public void Create_GrantsInstanceCreationToTheAccountsThatHostTheServer()
    {
        var security = PipeSecurityFactory.Create();
        Assert.IsNotNull(security);

        // The service runs two overlapping listen loops, so it creates further instances while the pipe
        // name is live and is access-checked against this DACL; denying it would break local search.
        Assert.IsTrue(GrantedRights(security, WellKnownSidType.LocalSystemSid)
            .HasFlag(PipeAccessRights.CreateNewInstance));
    }

    // A client never creates instances, so FILE_CREATE_PIPE_INSTANCE on a public ACE is purely
    // exploitable: any local process could add its own "LertaroPipe" instance and have the App's requests
    // answered by it.
    [TestMethod]
    [DataRow(WellKnownSidType.WorldSid)]
    [DataRow(WellKnownSidType.AuthenticatedUserSid)]
    public void Create_DeniesInstanceCreationButKeepsReadAccessForBroadPrincipals(WellKnownSidType kind)
    {
        var security = PipeSecurityFactory.Create();
        Assert.IsNotNull(security);

        var granted = GrantedRights(security, kind);
        Assert.IsFalse(granted.HasFlag(PipeAccessRights.CreateNewInstance), $"{kind} may create pipe instances");
        Assert.IsTrue(granted.HasFlag(PipeAccessRights.ReadWrite), $"{kind} lost read/write, local search would break");
    }
}
