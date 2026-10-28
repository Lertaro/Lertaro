using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

// The two decisions StartupElevationNotice makes about somebody else's registry key: which of its values is
// this program, and which of their flag lists asks for elevation. Both are read-only judgements about a table
// Windows owns the format of, and a wrong answer either stays silent about a broken autostart or, worse, tells
// the user their machine is misconfigured when it is not.
[TestClass]
public sealed class StartupElevationNoticeTests
{
    private const string Exe = @"C:\Program Files\Lertaro\Lertaro.App.exe";

    [TestMethod]
    [DataRow(Exe, true)]
    [DataRow(@"c:\program files\lertaro\lertaro.app.exe", true)]
    [DataRow("\"" + Exe + "\"", true)] // the Compatibility tab has been seen writing the quoted form
    [DataRow("  " + Exe + " ", true)]
    [DataRow(@"C:\Program Files\Lertaro\Lertaro.Service.exe", false)]
    [DataRow(@"C:\Lertaro\Lertaro.App.exe", false)]
    [DataRow("", false)]
    [DataRow("   ", false)]
    [DataRow("C:\a\0b", false)] // Path.GetFullPath rejects it; the catch, not a throw, is the answer
    public void IsThisExe_ComparesTheValueNameAsAPath(string valueName, bool expected) =>
        Assert.AreEqual(expected, StartupElevationNotice.IsThisExe(valueName, Exe));

    [TestMethod]
    [DataRow("~ RUNASADMIN", true)]
    [DataRow("RUNASADMIN", true)]
    [DataRow("runasadmin", true)]
    [DataRow("~ RUNASADMIN ~ PERPROCESSITEMLIST", true)]
    [DataRow("~ HIGHDPIAWARE", false)]
    [DataRow("~ RUNASINVOKER", false)] // the opposite instruction, and the one substring matching would love to confuse
    [DataRow("", false)]
    public void RequestsElevation_ReadsTheFlagList(string data, bool expected) =>
        Assert.AreEqual(expected, StartupElevationNotice.RequestsElevation(data));
}
