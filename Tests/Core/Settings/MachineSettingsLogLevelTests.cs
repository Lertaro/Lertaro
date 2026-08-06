namespace Lertaro.Core.Tests.Settings;

// The service's own log level, which is the only one it has: it runs as LocalSystem and cannot read the
// per-user setting the app and the hook both use, so before this it sat at a hardcoded default and
// every LogLevel.Debug line in the indexer was unreachable no matter what the settings page said.
//
// Hand-edited, so what is pinned here is that it is forgiving about how it is written. A "debug" that
// silently fell back would look exactly like the level having no effect, which is the symptom that made
// the setting necessary in the first place.
[TestClass]
public sealed class MachineSettingsLogLevelTests
{
    private static LogLevel Resolve(string? value) => new MachineSettings { ServiceLogLevel = value! }.ResolveServiceLogLevel();

    [TestMethod]
    public void EachLevelIsRecognised()
    {
        Assert.AreEqual(LogLevel.Error, Resolve("Error"));
        Assert.AreEqual(LogLevel.Warn, Resolve("Warn"));
        Assert.AreEqual(LogLevel.Info, Resolve("Info"));
        Assert.AreEqual(LogLevel.Debug, Resolve("Debug"));
    }

    [TestMethod]
    public void CaseAndSurroundingSpaceDoNotMatter()
    {
        Assert.AreEqual(LogLevel.Debug, Resolve("debug"));
        Assert.AreEqual(LogLevel.Debug, Resolve("DEBUG"));
        Assert.AreEqual(LogLevel.Debug, Resolve("  Debug  "));
    }

    // A machine nobody has touched still has to be diagnosable, so an untouched file logs at Info --
    // the same level the app and the hook run at.
    [TestMethod]
    public void AFileThatNeverMentionsItRunsAtInfo()
        => Assert.AreEqual(LogLevel.Info, new MachineSettings().ResolveServiceLogLevel());

    // Written but not understood is a typo, and answering a typo by going quiet would hide it behind a
    // silence indistinguishable from a deliberate "Error".
    [TestMethod]
    public void SomethingUnrecognisedIsAlsoInfo()
    {
        Assert.AreEqual(LogLevel.Info, Resolve("verbose"));
        Assert.AreEqual(LogLevel.Info, Resolve(""));
        Assert.AreEqual(LogLevel.Info, Resolve(null));
    }
}
