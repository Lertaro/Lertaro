using System.Diagnostics;
using Lertaro.Core.Services.Installation;

namespace Lertaro.Core.Tests.Services.Installation;

// Verify is the verdict the App acts on: an actual read/create/delete inside the data root, not a File.Exists
// or an Allow-ACE heuristic. It measures the folder and changes nothing in it, which is what these two tests
// pin -- a pass leaves no file behind, and a link is refused rather than followed.
[TestClass]
public sealed class UserDataAccessTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LertaroUserDataAccess_" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void Verify_LeavesAUsableDirectoryUsable()
    {
        Directory.CreateDirectory(_root);

        UserDataAccess.Verify(_root);

        // The probe file Verify creates is DeleteOnClose, so a successful call leaves nothing behind.
        Assert.IsEmpty(Directory.GetFiles(_root), "Verify must not leave its probe file behind");
    }

    [TestMethod]
    public void Verify_ADataRootThatIsALink_IsRefused()
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var link = Path.Combine(_root, "Lertaro");
        using (var process = Process.Start(new ProcessStartInfo("cmd.exe",
            $"/d /c mklink /J \"{link}\" \"{target}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!)
            process.WaitForExit();

        try
        {
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => UserDataAccess.Verify(link));
        }
        finally { Directory.Delete(link); }
    }
}
