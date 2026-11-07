using Lertaro.App.ViewModels.Settings.General;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

// Three fixed boxes over a list that persistence compacts. The mismatch is the whole risk: box 2 can hold
// the first mirror the updater tries, and an invalid address has to reach the settings window's validation
// count rather than be silently dropped, or Apply writes something the user never agreed to.
[TestClass]
public sealed class UpdateMirrorSettingsViewModelTests
{
    [TestMethod]
    public void Constructor_FillsTheSlotsInOrderAndLeavesTheRestEmpty()
    {
        var settings = new UserSettings { GitHubUpdateMirrors = ["https://one.example.com", "https://two.example.com/"] };

        var vm = new UpdateMirrorSettingsViewModel(settings);

        Assert.AreEqual("https://one.example.com/", vm.Mirror1);
        Assert.AreEqual("https://two.example.com/", vm.Mirror2);
        Assert.AreEqual(string.Empty, vm.Mirror3);
    }

    [TestMethod]
    public void Constructor_NormalizesAHandEditedFileBeforeShowingIt()
    {
        // user-settings.json is hand-editable and only TryParse normalizes it, so a file written by an
        // older build or by hand must not put a fourth mirror or a bare prefix into a box.
        var settings = new UserSettings
        {
            GitHubUpdateMirrors = ["  https://one.example.com///  ", "ftp://bad.example.com/", "https://two.example.com", "https://three.example.com", "https://four.example.com"],
        };

        var vm = new UpdateMirrorSettingsViewModel(settings);

        Assert.AreEqual("https://one.example.com/", vm.Mirror1);
        Assert.AreEqual("https://two.example.com/", vm.Mirror2);
        Assert.AreEqual("https://three.example.com/", vm.Mirror3);
    }

    [TestMethod]
    public void Edits_AreStagedUntilSave()
    {
        var settings = new UserSettings();
        var vm = new UpdateMirrorSettingsViewModel(settings) { Mirror1 = "https://one.example.com" };

        // Same staged-until-Apply contract the rest of the General page follows: Cancel must leave the
        // file alone.
        Assert.IsEmpty(settings.GitHubUpdateMirrors);

        vm.Save();

        CollectionAssert.AreEqual(new[] { "https://one.example.com/" }, settings.GitHubUpdateMirrors);
    }

    [TestMethod]
    public void Save_CompactsSoABlankFirstBoxDoesNotLeaveAHole()
    {
        var settings = new UserSettings();
        var vm = new UpdateMirrorSettingsViewModel(settings)
        {
            Mirror1 = "",
            Mirror2 = "https://two.example.com",
            Mirror3 = "https://three.example.com",
        };

        vm.Save();

        // The order of what was typed is the order it is tried in, so the filled boxes move up.
        CollectionAssert.AreEqual(
            new[] { "https://two.example.com/", "https://three.example.com/" },
            settings.GitHubUpdateMirrors);
    }

    [TestMethod]
    public void Save_AllBoxesEmpty_ClearsTheList()
    {
        var settings = new UserSettings { GitHubUpdateMirrors = ["https://one.example.com/"] };
        var vm = new UpdateMirrorSettingsViewModel(settings) { Mirror1 = "   " };

        vm.Save();

        Assert.IsEmpty(settings.GitHubUpdateMirrors);
    }

    [TestMethod]
    public void Save_TrimsWhatWasTypedAndCapsAtThree()
    {
        var settings = new UserSettings();
        var vm = new UpdateMirrorSettingsViewModel(settings)
        {
            Mirror1 = "  https://one.example.com  ",
            Mirror2 = "https://two.example.com/",
            Mirror3 = "https://three.example.com///",
        };

        vm.Save();

        CollectionAssert.AreEqual(
            new[] { "https://one.example.com/", "https://two.example.com/", "https://three.example.com/" },
            settings.GitHubUpdateMirrors);
    }

    [TestMethod]
    [DataRow("ftp://mirror.example.com/")]
    [DataRow("https://")]
    [DataRow("mirror.example.com")]
    [DataRow("javascript:alert(1)")]
    public void AnInvalidAddress_ThrowsSoTheWindowGatesApply(string typed)
    {
        var vm = new UpdateMirrorSettingsViewModel(new UserSettings());

        // Throwing is what raises Validation.Error in the XAML binding, which SettingsWindow counts into
        // CanApply. Dropping the value instead would let OK persist a mirror the user never saw accepted.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vm.Mirror2 = typed);
    }

    [TestMethod]
    public void AnInvalidAddress_LeavesTheSlotAtItsPreviousValue()
    {
        var vm = new UpdateMirrorSettingsViewModel(new UserSettings()) { Mirror1 = "https://one.example.com/" };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => vm.Mirror1 = "not a url");

        Assert.AreEqual("https://one.example.com/", vm.Mirror1);
    }

    [TestMethod]
    public void ClearingASlot_IsAcceptedAndRaisesPropertyChanged()
    {
        var vm = new UpdateMirrorSettingsViewModel(new UserSettings()) { Mirror1 = "https://one.example.com/" };
        var changed = string.Empty;
        vm.PropertyChanged += (_, e) => changed = e.PropertyName ?? string.Empty;

        vm.Mirror1 = "";

        Assert.AreEqual(string.Empty, vm.Mirror1);
        Assert.AreEqual(nameof(vm.Mirror1), changed);
    }

    [TestMethod]
    public void TheGeneralPageExposesTheSubViewModelTheSystemTabBindsTo()
    {
        // The XAML path is General.UpdateMirrors.Mirror1 and a name missing from the DataContext fails
        // silently at bind time, so pin the two hops the binding walks.
        var settings = new UserSettings { GitHubUpdateMirrors = ["https://one.example.com/"] };

        var general = new GeneralSettingsViewModel(settings);

        Assert.IsNotNull(general.UpdateMirrors);
        Assert.AreEqual("https://one.example.com/", general.UpdateMirrors.Mirror1);
        general.Cleanup();
    }
}