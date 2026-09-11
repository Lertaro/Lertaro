using System.IO;
using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

// The Settings window's Apply/OK has to save an open plugin config, so the user no longer has to press
// the plugin page's own Save Config button as a second step. That button stays (its original purpose
// upstream is unclear), so both routes run the same commit.
//
// The commit itself reaches process-wide hooks (InlineSearchManager.Instance, App.HookClient), so it is
// not constructible here; what these pin is the decision of WHEN Apply should write -- which is the part
// that was missing entirely before.
[TestClass]
public sealed class PluginConfigCommitSupportTests
{
    [TestMethod]
    public void PendingOnSettingsApply_OpenConfigTab_ReturnsThatPlugin()
    {
        var plugin = MakePlugin();
        plugin.IsConfigTab = true;

        Assert.AreSame(plugin, PluginConfigCommitSupport.PendingOnSettingsApply(plugin));
    }

    [TestMethod]
    public void PendingOnSettingsApply_DetailsTab_ReturnsNull()
    {
        // Edits are only ever staged on the config tab, and leaving it rolls the fields back
        // (see PluginInfoViewModel.IsConfigTab), so a plugin sitting on its details tab has nothing
        // pending -- writing it there would persist values the user had already discarded by navigating.
        var plugin = MakePlugin();
        plugin.IsConfigTab = true;
        plugin.IsConfigTab = false;

        Assert.IsNull(PluginConfigCommitSupport.PendingOnSettingsApply(plugin));
    }

    [TestMethod]
    public void PendingOnSettingsApply_NoSelection_ReturnsNull() => Assert.IsNull(PluginConfigCommitSupport.PendingOnSettingsApply(null));

    [TestMethod]
    public void SettingsApplyCommitsAnOpenPluginConfig_BeforePersisting()
    {
        // Save() writes DisabledPluginComponents through the shared UserSettings object and relies on
        // _userSettings.Save() having been called by the caller; committing the config after that save
        // would leave the field edits on disk only until the next unrelated save. So the commit has to
        // come first -- pinned here in file order rather than by running the real Save().
        var manager = Source("App/ViewModels/Settings/Plugins/PluginManagementViewModel.cs");

        var commit = manager.IndexOf("PluginConfigCommitSupport.Commit(", StringComparison.Ordinal);
        var disabledWrite = manager.IndexOf("_userSettings.DisabledPluginComponents = disabled.ToList();", StringComparison.Ordinal);

        Assert.IsGreaterThan(-1, commit, "Save() no longer commits an open plugin config");
        Assert.IsGreaterThan(-1, disabledWrite, "the component enable/disable write moved");
        Assert.IsLessThan(disabledWrite, commit,
            "the plugin config commit must run inside Save(), alongside the other staged writes");
        Assert.Contains("PendingOnSettingsApply(SelectedPlugin)", manager,
            "Save() must only commit a config that is actually open");
    }

    private static PluginInfoViewModel MakePlugin()
    {
        var field = new PluginConfigFieldViewModel(
            "p",
            new PluginSdk.Abstractions.PluginConfigField { Key = "k", FieldType = PluginSdk.Abstractions.ConfigFieldType.Text, DefaultValue = "" },
            new Core.UserSettings(),
            () => { });
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
