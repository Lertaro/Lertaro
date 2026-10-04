using Lertaro.Core.Services;
using Lertaro.Core.Wire;

namespace Lertaro.Core.Tests.Services;

// The SetMachineSettings request is how the interactive user's exclusion rules reach the service, which
// holds them in memory only (see UsnIndexer.WalkOptions). The decision covered here is which of the two
// "no rules" shapes a request is: one that never mentioned the rules, and one that says the user has
// none. Collapsing them either silently keeps filtering by rules the user deleted, or silently stops
// filtering by rules they still have.
[TestClass]
public sealed class UsnServicePipeRequestProcessorTests
{
    [TestMethod]
    public void ResolveExclusionRules_RequestThatMentionsNoLists_ReturnsNullSoHeldRulesSurvive()
    {
        var msg = new SearchRequestMessage
        {
            Id = SearchRequestId.SetMachineSettings,
            MachineSettings = new MachineSettings { LocalDrives = { "C" } }
        };

        Assert.IsNull(UsnServicePipeRequestProcessor.ResolveExclusionRules(msg));
    }

    [TestMethod]
    public void ResolveExclusionRules_ThreeEmptyLists_ReturnsAnEmptyRuleSetThatReplacesTheHeldOne()
    {
        var msg = new SearchRequestMessage
        {
            Id = SearchRequestId.SetMachineSettings,
            MachineSettings = new MachineSettings(),
            ExcludedPaths = [],
            IgnoredPathGlobs = [],
            IgnoredPathRegexes = []
        };

        var rules = UsnServicePipeRequestProcessor.ResolveExclusionRules(msg);

        Assert.IsNotNull(rules);
        Assert.IsEmpty(rules.ExcludedPaths);
        Assert.IsEmpty(rules.IgnoredPathGlobs);
        Assert.IsEmpty(rules.IgnoredPathRegexes);
    }

    [TestMethod]
    public void ResolveExclusionRules_CarriesAllThreeListsOntoTheWalk()
    {
        var msg = new SearchRequestMessage
        {
            Id = SearchRequestId.SetMachineSettings,
            MachineSettings = new MachineSettings(),
            ExcludedPaths = [@"C:\excluded", @"D:\also"],
            IgnoredPathGlobs = ["node_modules", "*.tmp"],
            IgnoredPathRegexes = ["^secret-"]
        };

        var rules = UsnServicePipeRequestProcessor.ResolveExclusionRules(msg);

        Assert.IsNotNull(rules);
        CollectionAssert.AreEqual(new[] { @"C:\excluded", @"D:\also" }, rules.ExcludedPaths.ToArray());
        CollectionAssert.AreEqual(new[] { "node_modules", "*.tmp" }, rules.IgnoredPathGlobs.ToArray());
        CollectionAssert.AreEqual(new[] { "^secret-" }, rules.IgnoredPathRegexes.ToArray());
    }

    // A request carrying only some of the three lists still counts as "the user's current rules": the
    // lists it omitted are read as empty rather than as "leave that one alone", since the App always sends
    // all three together and a partial one can only mean the rest genuinely hold nothing.
    [TestMethod]
    public void ResolveExclusionRules_OnlySomeListsPresent_TreatsTheOmittedOnesAsEmpty()
    {
        var msg = new SearchRequestMessage
        {
            Id = SearchRequestId.SetMachineSettings,
            MachineSettings = new MachineSettings(),
            ExcludedPaths = [@"C:\excluded"]
        };

        var rules = UsnServicePipeRequestProcessor.ResolveExclusionRules(msg);

        Assert.IsNotNull(rules);
        CollectionAssert.AreEqual(new[] { @"C:\excluded" }, rules.ExcludedPaths.ToArray());
        Assert.IsEmpty(rules.IgnoredPathGlobs);
        Assert.IsEmpty(rules.IgnoredPathRegexes);
    }

    // The rules are only ever in memory: the message that carries them must not have grown a field that
    // persists them, which is what upstream asked to be removed. Checked on the wire type itself, since a
    // reintroduced setting would otherwise quietly start writing a user's settings under a machine path.
    [TestMethod]
    public void SearchRequestMessage_IsTheOnlyCarrierOfTheRules()
    {
        var settingsFields = typeof(MachineSettings).GetProperties()
            .Select(p => p.Name)
            .Where(name => name.Contains("Excluded", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Ignored", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.IsEmpty(settingsFields,
            "the rules must travel over the request and stay in memory, not in machine settings; found: "
            + string.Join(", ", settingsFields));
    }
}
