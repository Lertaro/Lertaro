using System.IO;
using Lertaro.App.ViewModels.Settings;

namespace Lertaro.App.Tests.ViewModels.Settings;

// The Settings window must open without paying for the expensive tabs. Its PAGES were already built lazily
// (issue #186), but the ViewModel constructor still built every sub-VM, so opening the window paid for all
// of it anyway -- which is what made every tab feel sluggish from the start, not just the first.
//
// The three deferred ones each do real work in their constructors (file reads / theme resource loading), so
// these pin that deferral, and -- the part that is easy to break silently -- that nothing on a recurring
// path reaches through the property and constructs one anyway.
[TestClass]
public sealed class DeferredSettingsViewModelsTests
{
    [TestMethod]
    public void TheHeavyViewModels_AreDeferredRatherThanBuiltInTheConstructor()
    {
        var vm = Source("App/ViewModels/Settings/SettingsViewModel.cs");

        // Each is exposed through the holder instead of being assigned in the constructor.
        Assert.Contains("_deferred.Log", vm);
        Assert.Contains("_deferred.Appearance", vm);
        Assert.Contains("_deferred.History", vm);

        var ctor = Between(vm, "public SettingsViewModel()", "// The three DEFERRED sub-VMs");
        Assert.DoesNotContain("new ServiceLogViewModel", ctor,
            "the log reader reads up to 500 lines of app.log and must not be built at window open");
        Assert.DoesNotContain("new ThemeSettingsViewModel", ctor,
            "the theme list loads a ResourceDictionary per theme and must not be built at window open");
        Assert.DoesNotContain("new HistorySettingsViewModel", ctor,
            "the history lists read their files and must not be built at window open");
    }

    [TestMethod]
    public void TheRecurringStatusPath_DoesNotConstructTheLogReader()
    {
        // ApplyUiState runs on a 5s timer and on every status push (up to ~10/s while a drive indexes). It
        // used to set Log.IsServiceReady, which would construct the log reader -- i.e. the deferral would be
        // undone by a timer the user never triggered. It must go through the existing-only accessor.
        var vm = Source("App/ViewModels/Settings/SettingsViewModel.cs");
        var applyUiState = Between(vm, "private void ApplyUiState()", "\n    }");

        Assert.DoesNotContain("Log.IsServiceReady", applyUiState,
            "the periodic status path must not force the deferred log reader into existence");
    }

    [TestMethod]
    public void CleanupOnlyTouchesWhatWasActuallyBuilt()
    {
        // Closing the window without visiting History/Appearance must not construct them just to clean up.
        var vm = Source("App/ViewModels/Settings/SettingsViewModel.cs");
        var cleanup = Between(vm, "public void Cleanup()", "\n    }");

        Assert.DoesNotContain("Appearance.Cleanup()", cleanup, "must use the existing-only accessor");
        Assert.DoesNotContain("Log.Dispose()", cleanup, "must use the existing-only accessor");
        Assert.Contains("ExistingAppearance", cleanup);
        Assert.Contains("ExistingLog", cleanup);
    }

    [TestMethod]
    public void SaveOnApply_DoesNotThrowAwayAnUntouchedHistoryTab()
    {
        // Apply() saves what is staged. An unvisited History tab has nothing staged, so reaching through the
        // property would construct it (loading both history files) purely to write back what it just read.
        var vm = Source("App/ViewModels/Settings/SettingsViewModel.cs");

        Assert.Contains("ExistingHistory?.Save()", vm,
            "Apply must save only a History tab that was actually built");
    }

    private static string Between(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, start, $"could not find '{from}'");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, end, $"could not find '{to}' after '{from}'");
        return source.Substring(start, end - start);
    }

    private static string Source(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        var path = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.IsTrue(File.Exists(path), $"expected a file at {path}");
        return File.ReadAllText(path);
    }
}
