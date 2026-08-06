using Lertaro.Plugins.CoreExtensions.Providers.QuickPanel;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.QuickPanel;

// The Recent folder is read through Build, which takes the folder and the shortcut resolver as
// arguments: the real one goes to %AppData%\...\Recent and through COM, neither of which a test can set
// up. What is tested is everything that decides which entries come back and in what order.
[TestClass]
public sealed class WindowsRecentTabProviderTests
{
    [TestMethod]
    public void Build_OrdersByShortcutTimeAndAppliesTheCapToTheNewest()
    {
        using var recent = new TempDirectory();
        using var files = new TempDirectory();

        // Created oldest first, so the order the directory hands them over in is the wrong answer.
        var oldest = Shortcut(recent, files, "oldest", DateTime.Now.AddHours(-3));
        var middle = Shortcut(recent, files, "middle", DateTime.Now.AddHours(-2));
        var newest = Shortcut(recent, files, "newest", DateTime.Now.AddHours(-1));

        var entries = WindowsRecentTabProvider.Build(recent.Path, maxItems: 2, ResolveInto(files));

        Assert.HasCount(2, entries);
        Assert.AreEqual(newest, entries[0].FullPath);
        Assert.AreEqual(middle, entries[1].FullPath);
        Assert.IsFalse(entries.Any(entry => entry.FullPath == oldest), "the cap keeps the newest, not the first found");
    }

    [TestMethod]
    public void Build_CarriesTheShortcutsOwnTimeAsTheModifiedTime()
    {
        using var recent = new TempDirectory();
        using var files = new TempDirectory();

        var opened = DateTime.Now.AddMinutes(-5);
        Shortcut(recent, files, "report", opened);

        var entries = WindowsRecentTabProvider.Build(recent.Path, maxItems: 10, ResolveInto(files));

        // The panel orders a group by this, and a document read but never edited has a file time from
        // long before it was last opened.
        Assert.AreEqual(
            opened.ToString("yyyy-MM-dd HH:mm"),
            entries.Single().Metadata.Modified.ToString("yyyy-MM-dd HH:mm"));
    }

    [TestMethod]
    public void Build_SkipsShortcutsWhoseTargetIsGoneOrUnresolvable()
    {
        using var recent = new TempDirectory();
        using var files = new TempDirectory();

        Shortcut(recent, files, "alive", DateTime.Now);
        Write(recent, "deleted.lnk", DateTime.Now);
        Write(recent, "unresolvable.lnk", DateTime.Now);

        // "deleted" resolves to a path that was never created: the shell leaves those shortcuts behind
        // for weeks after the file goes.
        var entries = WindowsRecentTabProvider.Build(recent.Path, maxItems: 10, shortcut =>
            Path.GetFileName(shortcut) == "unresolvable.lnk" ? null : ResolveInto(files)(shortcut));

        Assert.HasCount(1, entries);
        StringAssert.EndsWith(entries.Single().FullPath, "alive");
    }

    [TestMethod]
    public void Build_KeepsOnlyOneEntryPerFileWhenTwoShortcutsPointAtIt()
    {
        using var recent = new TempDirectory();
        using var files = new TempDirectory();

        var target = Path.Combine(files.Path, "shared");
        File.WriteAllText(target, string.Empty);
        Write(recent, "opened-once.lnk", DateTime.Now.AddHours(-2));
        Write(recent, "opened-again.lnk", DateTime.Now);

        var entries = WindowsRecentTabProvider.Build(recent.Path, maxItems: 10, _ => target);

        Assert.HasCount(1, entries);
    }

    [TestMethod]
    public void Build_MissingRecentFolder_IsEmptyRatherThanAThrow()
    {
        using var files = new TempDirectory();

        Assert.IsEmpty(WindowsRecentTabProvider.Build(
            Path.Combine(Path.GetTempPath(), "lertaro-no-such-folder"), maxItems: 10, ResolveInto(files)));
    }

    [TestMethod]
    public void Build_IgnoresAnythingThatIsNotAShortcut()
    {
        using var recent = new TempDirectory();
        using var files = new TempDirectory();
        File.WriteAllText(Path.Combine(recent.Path, "desktop.ini"), string.Empty);

        Assert.IsEmpty(WindowsRecentTabProvider.Build(recent.Path, maxItems: 10, ResolveInto(files)));
    }

    // A shortcut named "<name>.lnk" in the Recent folder, standing for a real file of that name, with a
    // write time saying when it was opened. ResolveInto is the stand-in resolver that undoes it.
    private static string Shortcut(TempDirectory recent, TempDirectory files, string name, DateTime openedAt)
    {
        var target = Path.Combine(files.Path, name);
        File.WriteAllText(target, string.Empty);
        Write(recent, name + ".lnk", openedAt);
        return target;
    }

    private static void Write(TempDirectory recent, string fileName, DateTime writtenAt)
    {
        var path = Path.Combine(recent.Path, fileName);
        File.WriteAllText(path, string.Empty);
        File.SetLastWriteTime(path, writtenAt);
    }

    private static Func<string, string?> ResolveInto(TempDirectory files)
        => shortcut => Path.Combine(files.Path, Path.GetFileNameWithoutExtension(shortcut));

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
