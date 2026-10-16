using System.Diagnostics;
using System.IO;
using Lertaro.App.Services;
using Lertaro.Core.Services.Installation;

namespace Lertaro.App.Tests.Services.ServiceManagement;

// Waiting out a lock instead of escalating it. A settings file another instance is still saving used to send
// the App into an elevated, tree-wide ACL repair on the very first failure -- and that repair is also the only
// thing that can unblock the start, so one busy file could stop the App from starting at all.
[TestClass]
public sealed class AppStartupServiceBootstrapperTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LertaroStartupVerify_" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task VerifyUserDataAsync_LockThatGoesAway_IsWaitedOutRatherThanEscalated()
    {
        Directory.CreateDirectory(_root);
        var settings = Path.Combine(_root, "user-settings.json");
        File.WriteAllText(settings, "{}");
        using var busy = new FileStream(settings, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(400);
            busy.Dispose();
        });

        // The lock has to be real, or the wait below proves nothing: one look has to fail while it is held.
        Assert.ThrowsExactly<IOException>(() => UserDataAccess.Verify(_root));

        // Production waits 6 x 250 ms; fewer and shorter here so the test does not sit for seconds.
        var usable = await AppStartupServiceBootstrapper.VerifyUserDataAsync(
            _root, attempts: 12, delay: TimeSpan.FromMilliseconds(50));

        await release;
        Assert.IsTrue(usable, "a lock that goes away is not a reason to show a UAC prompt");
    }

    [TestMethod]
    public async Task VerifyUserDataAsync_ADataRootThatIsALink_IsNotWaitedFor()
    {
        Directory.CreateDirectory(Path.Combine(_root, "target"));
        var link = Path.Combine(_root, "Lertaro");
        Mklink("/J", link, Path.Combine(_root, "target"));
        try
        {
            // A link is what the elevated repair exists for and waiting cannot change it, so the check has to
            // come back on the first look rather than spend the whole retry budget first.
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => UserDataAccess.Verify(link));
            Assert.IsFalse(await AppStartupServiceBootstrapper.VerifyUserDataAsync(
                link, attempts: 6, delay: TimeSpan.FromMilliseconds(250)));
        }
        finally { Directory.Delete(link); }
    }

    private static void Mklink(string kind, string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c mklink {kind} \"{link}\" \"{target}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode);
    }
}
