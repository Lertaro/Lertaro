using Lertaro.App.Services.ShellMenu.Presenter;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Services.ShellMenu.Presenter;

// The rule that decides whether an actions menu may open for an INSTANT result.
//
// Reported bug it exists for: the window switcher's window menu was fully implemented but never
// appeared, because the host refuses the actions menu for instant results -- and those rows ARE instant
// results. The gate is now opt-in per provider, and specific to the selection the provider claims, so a
// different plugin's instant result cannot open an empty menu.
[TestClass]
public sealed class ActionsMenuEligibilityTests
{
    private sealed class FakeProvider : IDynamicActionProvider
    {
        public string GroupName => "Fake";
        public bool CanProvideForInstantResults { get; init; }
        public bool Claims { get; init; }
        public bool Throws { get; init; }

        public bool CanProvide(IReadOnlyList<ISearchResult> results) =>
            Throws ? throw new InvalidOperationException("provider blew up") : Claims;

        public IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, IntPtr hMenu) => [];
        public void ExecuteCommand(IReadOnlyList<ISearchResult> results, uint commandId, IntPtr ownerHwnd) { }
        public void ClearSession() { }
    }

    private static AppSearchResult WindowRow() => new()
    {
        Name = "Notepad",
        FullPath = string.Empty,
        ResultKind = "InstantResult"
    };

    // A provider that declares it acts on instant results and claims this one opens the menu.
    [TestMethod]
    public void AllowsInstantResults_OptedInProviderClaimsTheSelection_Allows()
    {
        var providers = new IDynamicActionProvider[] { new FakeProvider { CanProvideForInstantResults = true, Claims = true } };

        Assert.IsTrue(ActionsMenuEligibility.AllowsInstantResults(providers, new[] { WindowRow() }));
    }

    // The declaration alone is not enough: a provider that opts in but declines THIS result must not open
    // an empty menu -- that is the difference between "the window switcher's rows" and "any instant row".
    [TestMethod]
    public void AllowsInstantResults_OptedInButDoesNotClaimThisSelection_Denies()
    {
        var providers = new IDynamicActionProvider[] { new FakeProvider { CanProvideForInstantResults = true, Claims = false } };

        Assert.IsFalse(ActionsMenuEligibility.AllowsInstantResults(providers, new[] { WindowRow() }));
    }

    // Every existing provider keeps the old behaviour without being touched: no opt-in, no menu.
    [TestMethod]
    public void AllowsInstantResults_ProviderDidNotOptIn_DeniesEvenWhenItClaims()
    {
        var providers = new IDynamicActionProvider[] { new FakeProvider { CanProvideForInstantResults = false, Claims = true } };

        Assert.IsFalse(ActionsMenuEligibility.AllowsInstantResults(providers, new[] { WindowRow() }));
    }

    // A second provider still gets its say when an earlier one throws.
    [TestMethod]
    public void AllowsInstantResults_ThrowingProvider_DoesNotStopTheOthers()
    {
        var providers = new IDynamicActionProvider[]
        {
            new FakeProvider { CanProvideForInstantResults = true, Throws = true },
            new FakeProvider { CanProvideForInstantResults = true, Claims = true }
        };

        Assert.IsTrue(ActionsMenuEligibility.AllowsInstantResults(providers, new[] { WindowRow() }));
    }

    [TestMethod]
    public void AllowsInstantResults_NoProvidersAtAll_Denies() => Assert.IsFalse(ActionsMenuEligibility.AllowsInstantResults([], new[] { WindowRow() }));
}
