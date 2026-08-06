using System.ComponentModel;

using Lertaro.App.ViewModels.Settings.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.QuickPanel;

// Switching language with the settings window open. Every label on this page is built in code -- the kind
// dropdown's options, a plugin's name for its own tab, an unnamed workspace's fallback -- so none of them
// repaint the way a XAML-bound string does, and the page went on showing the language it was opened in.
// A test can't load real translations (TranslationManager answers "[key]" here), so what is pinned is the
// part that broke: that the strings are re-asked at all, and that the page says so.
[TestClass]
public sealed class QuickPanelLanguageSwitchTests
{
    private static UserSettings BuildSettings()
    {
        var tab = new QuickPanelTab { Id = "tab1" };
        tab.Folders.Add(QuickPanelFolderSource.For(@"C:\a"));

        var settings = new UserSettings();
        settings.QuickPanel.Tabs = new List<QuickPanelTab> { tab };
        settings.QuickPanel.ActiveTabId = tab.Id;
        return settings;
    }

    private static List<string> Watch(INotifyPropertyChanged source)
    {
        var seen = new List<string>();
        source.PropertyChanged += (_, e) => seen.Add(e.PropertyName ?? string.Empty);
        return seen;
    }

    // The name a provider hands over is only correct for the language that was active when it was read,
    // which is why the row holds the provider rather than the string it gave.
    [TestMethod]
    public void APluginTabAsksItsProviderForTheNameEveryTime()
    {
        var language = "zh";
        var option = new QuickPanelPluginTabOption("id", () => $"name-{language}", isOpen: true, showAsList: false);

        Assert.AreEqual("name-zh", option.Name);
        language = "ko";
        Assert.AreEqual("name-ko", option.Name);
    }

    [TestMethod]
    public void APluginTabRowRepaintsItsName()
    {
        var option = new QuickPanelPluginTabOption("id", () => "name", isOpen: true, showAsList: false);
        var seen = Watch(option);

        option.NotifyLanguageChanged();

        CollectionAssert.Contains(seen, nameof(QuickPanelPluginTabOption.Name));
    }

    // The dropdown's labels are cached (they are only rebuilt for a language switch), so this is the one
    // that silently kept the old language on screen.
    [TestMethod]
    public void ASourceRowRepaintsItsKindDropdown()
    {
        var vm = new QuickPanelSettingsViewModel(BuildSettings());
        var row = vm.Tabs.Single().Sources.First();
        var seen = Watch(row);

        vm.NotifyLanguageChanged();

        CollectionAssert.Contains(seen, nameof(QuickPanelSourceRowViewModel.KindOptions));
    }

    // A workspace the user never renamed shows a translated fallback, so it moves language too.
    [TestMethod]
    public void AnUnnamedWorkspaceRepaintsItsFallbackName()
    {
        var vm = new QuickPanelSettingsViewModel(BuildSettings());
        var tab = vm.Tabs.Single();
        var seen = Watch(tab);

        vm.NotifyLanguageChanged();

        CollectionAssert.Contains(seen, nameof(QuickPanelTabSettingsViewModel.EffectiveName));
    }

    // Walked live rather than from a list captured when the page was built: a workspace added after the
    // window opened is exactly the one whose fallback name is still in the old language.
    [TestMethod]
    public void AWorkspaceAddedAfterThePageWasBuiltRepaintsToo()
    {
        var vm = new QuickPanelSettingsViewModel(BuildSettings());
        vm.AddTabCommand.Execute(null);
        var added = vm.Tabs.Last();
        var seen = Watch(added);

        vm.NotifyLanguageChanged();

        CollectionAssert.Contains(seen, nameof(QuickPanelTabSettingsViewModel.EffectiveName));
    }
}
