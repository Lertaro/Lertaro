using Lertaro.Core.Services.Update;

namespace Lertaro.Core.Tests.Services.Update;

[TestClass]
public sealed class UpdateRelaunchMarkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);

    private static string Note(DateTimeOffset writtenAt, string sessionId = "1",
        string appPath = @"C:\Program Files\Lertaro\Lertaro.App.exe") =>
        $"{writtenAt.UtcTicks}\t{sessionId}\t{appPath}";

    [TestMethod]
    public void TryParse_FreshNote_ReturnsSessionAndPath()
    {
        var parsed = UpdateRelaunchMarker.TryParse(Note(Now - TimeSpan.FromSeconds(20)), Now, out var sessionId, out var appPath);

        Assert.IsTrue(parsed);
        Assert.AreEqual(1, sessionId);
        Assert.AreEqual(@"C:\Program Files\Lertaro\Lertaro.App.exe", appPath);
    }

    [TestMethod]
    public void TryParse_PathWithSpaces_IsKeptIntact()
    {
        // The path is the tail field and is never split further, so the ordinary "Program Files" space costs
        // nothing here -- the service hands the string straight to CreateProcessAsUser.
        Assert.IsTrue(UpdateRelaunchMarker.TryParse(Note(Now, appPath: @"C:\a b\Lertaro.App.exe"), Now, out _, out var appPath));
        Assert.AreEqual(@"C:\a b\Lertaro.App.exe", appPath);
    }

    [TestMethod]
    public void TryParse_NoteOlderThanTheWindow_IsDiscarded() =>
        Assert.IsFalse(UpdateRelaunchMarker.TryParse(
            Note(Now - UpdateRelaunchMarker.FreshFor - TimeSpan.FromMilliseconds(1)), Now, out _, out _));

    [TestMethod]
    public void TryParse_NoteWrittenAfterTheServiceStarted_IsDiscarded() =>
        // A clock that moved backwards since the note must not park the relaunch until the note ages out.
        Assert.IsFalse(UpdateRelaunchMarker.TryParse(Note(Now + TimeSpan.FromDays(1)), Now, out _, out _));

    [DataRow("", "no fields at all")]
    [DataRow("123", "no session or path")]
    [DataRow("123\t1", "no path")]
    [DataRow("123\t1\t", "empty path")]
    [DataRow("not-a-tick\t1\tC:\\app.exe", "unreadable timestamp")]
    [DataRow("0\t1\tC:\\app.exe", "zero timestamp")]
    [DataRow("-1\t1\tC:\\app.exe", "negative timestamp")]
    [DataRow("9223372036854775807\t1\tC:\\app.exe", "timestamp past DateTimeOffset's range")]
    [DataRow("123\tnot-a-session\tC:\\app.exe", "unreadable session id")]
    [DataRow("123\t0\tC:\\app.exe", "session zero is not a logon session")]
    [DataRow("123\t-1\tC:\\app.exe", "negative session id")]
    [DataTestMethod]
    public void TryParse_MalformedNote_IsRefusedWithoutThrowing(string content, string because)
    {
        Assert.IsFalse(UpdateRelaunchMarker.TryParse(content, Now, out var sessionId, out var appPath), because);
        Assert.AreEqual(0, sessionId);
        Assert.AreEqual(string.Empty, appPath);
    }
}
