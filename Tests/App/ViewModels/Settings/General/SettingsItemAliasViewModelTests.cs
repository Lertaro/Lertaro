using Lertaro.App.ViewModels.Settings.General;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

// The row list is the only place a hand-typed alias gets turned into the dictionary the search reads, so
// what matters here is what Save() refuses to write: half-typed rows, untrimmed padding, and a second row
// for a word that is already taken.
[TestClass]
public sealed class SettingsItemAliasViewModelTests
{
    [TestMethod]
    public void Save_WritesTrimmedPairs()
    {
        var settings = new UserSettings();
        var vm = new SettingsItemAliasViewModel(settings);
        vm.Items.Add(new SettingsItemAliasItem(" env ", " 编辑系统环境变量 "));

        vm.Save();

        Assert.AreEqual("编辑系统环境变量", settings.SettingsItemAliases["env"]);
    }

    [TestMethod]
    public void Save_DropsHalfTypedRows()
    {
        var settings = new UserSettings();
        var vm = new SettingsItemAliasViewModel(settings);
        vm.Items.Add(new SettingsItemAliasItem("env", "   "));
        vm.Items.Add(new SettingsItemAliasItem("", "事件查看器"));
        vm.Items.Add(new SettingsItemAliasItem("log", "事件查看器"));

        vm.Save();

        Assert.HasCount(1, settings.SettingsItemAliases);
        Assert.AreEqual("事件查看器", settings.SettingsItemAliases["log"]);
    }

    [TestMethod]
    public void Save_SecondRowForSameWord_KeepsTheFirst()
    {
        var settings = new UserSettings();
        var vm = new SettingsItemAliasViewModel(settings);
        vm.Items.Add(new SettingsItemAliasItem("env", "编辑系统环境变量"));
        vm.Items.Add(new SettingsItemAliasItem("ENV", "编辑账户的环境变量"));

        vm.Save();

        Assert.HasCount(1, settings.SettingsItemAliases);
        Assert.AreEqual("编辑系统环境变量", settings.SettingsItemAliases["env"]);
    }

    [TestMethod]
    public void Save_RemovesTheRowTheUserDeleted()
    {
        var settings = new UserSettings();
        settings.SettingsItemAliases["log"] = "事件查看器";
        var vm = new SettingsItemAliasViewModel(settings);
        Assert.HasCount(1, vm.Items);

        vm.Items.RemoveAt(0);
        vm.Save();

        Assert.IsEmpty(settings.SettingsItemAliases);
    }

    [TestMethod]
    public void Constructor_SeedsOneRowPerSavedEntry()
    {
        var settings = new UserSettings();
        settings.SettingsItemAliases["env"] = "编辑系统环境变量";
        settings.SettingsItemAliases["log"] = "事件查看器";

        var vm = new SettingsItemAliasViewModel(settings);

        Assert.HasCount(2, vm.Items);
        CollectionAssert.AreEquivalent(
            new[] { "env", "log" },
            vm.Items.Select(item => item.Alias).ToArray());
    }
}
