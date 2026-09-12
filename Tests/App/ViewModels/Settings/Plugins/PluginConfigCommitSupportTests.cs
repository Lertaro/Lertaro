using System.IO;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

// The Settings window's Apply/OK is the single commit point for plugin configuration. Edits are staged
// per plugin and survive switching plugins, so the commit has to walk EVERY plugin holding staged edits,
// not just whichever one is on screen -- otherwise a plugin edited and then navigated away from would
// never be written at all.
//
// The commit itself reaches process-wide hooks (InlineSearchManager.Instance, App.HookClient), so the
// real CommitPending is not constructible here; what these pin is the decision of WHICH plugins it
// selects and in what order the persistence steps must run.
[TestClass]
public sealed class PluginConfigCommitSupportTests
{
    [TestMethod]
    public void HasPendingConfigEdits_TracksOnlyActualEdits()
    {
        var plugin = MakePlugin();
        Assert.IsFalse(plugin.HasPendingConfigEdits, "a freshly built plugin has nothing staged");

        // Opening the config tab alone is not an edit -- otherwise merely looking at a plugin would
        // commit it (and run its OnSave hook) on the next Apply.
        plugin.IsConfigTab = true;
        Assert.IsFalse(plugin.HasPendingConfigEdits, "opening a config is not editing it");

        plugin.ConfigFields[0].Value = "typed";
        Assert.IsTrue(plugin.HasPendingConfigEdits);
    }

    [TestMethod]
    public void SettingsApplyCommitsPendingPluginConfigs_BeforePersisting()
    {
        // Save() writes DisabledPluginComponents through the shared UserSettings object and relies on
        // _userSettings.Save() having been called by the caller; committing the config after that save
        // would leave the field edits on disk only until the next unrelated save. So the commit has to
        // come first -- pinned here in file order rather than by running the real Save().
        var manager = Source("App/ViewModels/Settings/Plugins/PluginManagementViewModel.cs");

        var commit = manager.IndexOf("PluginConfigCommitSupport.CommitPending(", StringComparison.Ordinal);
        var disabledWrite = manager.IndexOf("_userSettings.DisabledPluginComponents = disabled.ToList();", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, commit, "Save() no longer commits plugin configs");
        Assert.IsGreaterThan(-1, disabledWrite, "the component enable/disable write moved");
        Assert.IsLessThan(commit, disabledWrite,
            "the plugin config commit must run inside Save(), alongside the other staged writes");
        Assert.Contains("CommitPending(Plugins)", manager,
            "Save() must commit every plugin holding staged edits, not just the one on screen");
    }

    [TestMethod]
    public void CommitPending_IsGatedOnRealEdits()
    {
        // A plugin's OnSave hook can be expensive (ContentSearch triggers a full re-index), so the
        // commit must skip plugins the user only looked at. Checked at the source level because the
        // method's own body reaches process-wide hooks; HasPendingConfigEdits is the gate.
        var support = Source("App/ViewModels/Settings/Plugins/PluginConfigCommitSupport.cs");

        Assert.Contains("HasPendingConfigEdits", support, "the commit must be gated on real staged edits");
        Assert.Contains("OnSave", support, "the plugin's OnSave hook must run on commit");
    }

    private static PluginInfoViewModel MakePlugin()
    {
        var field = new PluginConfigFieldViewModel(
            "p",
            new PluginSdk.Abstractions.PluginConfigField { Key = "k", FieldType = PluginSdk.Abstractions.ConfigFieldType.Text, DefaultValue = "" },
            new UserSettings());
        return new PluginInfoViewModel("P", "1.0", "P.dll", "1.0-sdk", [], [field]);
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
