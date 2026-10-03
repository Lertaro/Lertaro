using System.Security.Principal;
using Lertaro.Core.Services.Update;

namespace Lertaro.Core.Tests.Services.Update;

[TestClass]
public sealed class UpdateRelaunchMarkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);

    private static string Note(DateTimeOffset writtenAt, string sessionId = "1") => $"{writtenAt.UtcTicks}\t{sessionId}";

    [TestMethod]
    public void TryParse_FreshNote_ReturnsSession()
    {
        var parsed = UpdateRelaunchMarker.TryParse(Note(Now - TimeSpan.FromSeconds(20), "3"), Now, out var sessionId);

        Assert.IsTrue(parsed);
        Assert.AreEqual(3, sessionId);
    }

    [DataRow(@"C:\Program Files\Lertaro\Lertaro.App.exe", "the path an older service wrote")]
    [DataRow(@"C:\Users\Public\evil.exe", "a path someone else would like started")]
    [DataRow("", "an empty trailing field")]
    [TestMethod]
    public void TryParse_NoteWithAPathField_ParsesTheSessionAndIgnoresThePath(string path, string because)
    {
        // The service being replaced by an update wrote ticks, session and App path. Its note must still bring
        // the App back, but the path is never used: the service always starts its own install's App.
        Assert.IsTrue(UpdateRelaunchMarker.TryParse($"{Note(Now)}\t{path}", Now, out var sessionId), because);
        Assert.AreEqual(1, sessionId);
    }

    [TestMethod]
    public void TryParse_NoteOlderThanTheWindow_IsDiscarded() =>
        Assert.IsFalse(UpdateRelaunchMarker.TryParse(
            Note(Now - UpdateRelaunchMarker.FreshFor - TimeSpan.FromMilliseconds(1)), Now, out _));

    [TestMethod]
    public void TryParse_NoteWrittenAfterTheServiceStarted_IsDiscarded() =>
        // A clock that moved backwards since the note must not park the relaunch until the note ages out.
        Assert.IsFalse(UpdateRelaunchMarker.TryParse(Note(Now + TimeSpan.FromDays(1)), Now, out _));

    [DataRow("", "no fields at all")]
    [DataRow("123", "no session")]
    [DataRow("not-a-tick\t1", "unreadable timestamp")]
    [DataRow("0\t1", "zero timestamp")]
    [DataRow("-1\t1", "negative timestamp")]
    [DataRow("9223372036854775807\t1", "timestamp past DateTimeOffset's range")]
    [DataRow("123\tnot-a-session", "unreadable session id")]
    [DataRow("123\t0", "session zero is not a logon session")]
    [DataRow("123\t-1", "negative session id")]
    [DataRow("123\t\tC:\\app.exe", "empty session field")]
    [TestMethod]
    public void TryParse_MalformedNote_IsRefusedWithoutThrowing(string content, string because)
    {
        Assert.IsFalse(UpdateRelaunchMarker.TryParse(content, Now, out var sessionId), because);
        Assert.AreEqual(0, sessionId);
    }

    [TestMethod]
    public void IsTrustedOwner_SystemAndAdministrators_AreTrusted()
    {
        Assert.IsTrue(UpdateRelaunchMarker.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)));
        Assert.IsTrue(UpdateRelaunchMarker.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)));
    }

    [DataRow("S-1-5-21-1000000000-2000000000-3000000000-1001", "an ordinary local account")]
    [DataRow("S-1-5-32-545", "BUILTIN\\Users")]
    [DataRow("S-1-5-11", "Authenticated Users")]
    [DataRow("S-1-1-0", "Everyone")]
    [TestMethod]
    public void IsTrustedOwner_AnyoneElse_IsNotTrusted(string sid, string because) =>
        Assert.IsFalse(UpdateRelaunchMarker.IsTrustedOwner(new SecurityIdentifier(sid)), because);
}
