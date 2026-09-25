using Lertaro.App.Services.Update;

namespace Lertaro.App.Tests.Services.Update;

/// <summary>
/// Each case drives its own instance. Production has one (only the update task ever writes it, so there is
/// nothing to contend with), but MSTest runs methods across twelve workers and a shared instance made the
/// cases see each other's writes.
/// </summary>
[TestClass]
public sealed class SilentUpdateStateTests
{
    [TestMethod]
    public void Text_ActivatesAndAnnouncesBothBindings()
    {
        var state = new SilentUpdateState();
        var raised = new List<string?>();
        state.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        state.Text = "Downloading update (42%)...";

        Assert.IsTrue(state.IsActive);
        // The line binds Text and its visibility binds IsActive, so a change announcing only one of the two
        // would leave either a hidden label or a stale visibility behind.
        CollectionAssert.Contains(raised, nameof(SilentUpdateState.Text));
        CollectionAssert.Contains(raised, nameof(SilentUpdateState.IsActive));
    }

    [TestMethod]
    public void Text_Empty_IsTheRestingState()
    {
        var state = new SilentUpdateState { Text = "Downloading update (42%)..." };

        state.Text = string.Empty;

        Assert.IsFalse(state.IsActive);
    }

    [TestMethod]
    public void Text_SameValueTwice_DoesNotAnnounceAChange()
    {
        var state = new SilentUpdateState { Text = "Downloading update (42%)..." };
        var raised = new List<string?>();
        state.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // The download loop reports per chunk; a percent that has not moved must not repaint the row.
        state.Text = "Downloading update (42%)...";

        Assert.IsEmpty(raised);
    }

    [TestMethod]
    [DoNotParallelize]
    public void Report_WritesThroughToTheBoundInstance_AndNullReturnsItToRest()
    {
        SilentUpdateState.Report("Downloading update (42%)...");

        Assert.AreEqual("Downloading update (42%)...", SilentUpdateState.Current.Text);
        Assert.IsTrue(SilentUpdateState.Current.IsActive);

        SilentUpdateState.Report(null);

        Assert.AreEqual(string.Empty, SilentUpdateState.Current.Text);
        Assert.IsFalse(SilentUpdateState.Current.IsActive);
    }
}
