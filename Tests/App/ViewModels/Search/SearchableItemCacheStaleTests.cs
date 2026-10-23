using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search;

// Stale provider entries remain available during reload.
[TestClass]
public sealed class SearchableItemCacheStaleTests
{
    private static string NewProviderId() => "TestProvider_" + Guid.NewGuid().ToString("N");

    [TestMethod]
    public void InvalidatingKeepsServingWhatItAlreadyHad()
    {
        var id = NewProviderId();
        SearchableItemCache.Seed(id, new List<SearchableItemCache.CacheEntry>
        {
            new(new PluginSdk.Abstractions.Plugins.SearchableItem { Title = "Notepad" }, new List<string>(), null),
        });

        SearchableItemCache.Invalidate(id);

        Assert.IsTrue(SearchableItemCache.TryGetEntries(id, out var entries), "the provider dropped out of results while it reloaded");
        Assert.HasCount(1, entries);
    }

    // Marked stale, so the next EnsureLoaded rebuilds rather than deciding it is already cached.
    [TestMethod]
    public void InvalidatingStillMarksItForReload()
    {
        var id = NewProviderId();
        SearchableItemCache.Seed(id, new List<SearchableItemCache.CacheEntry>());

        SearchableItemCache.Invalidate(id);

        Assert.IsTrue(SearchableItemCache.IsStale(id));
    }

    // Nothing cached yet means nothing to serve; there is no state worth carrying and the first load
    // has not run.
    [TestMethod]
    public void InvalidatingSomethingNeverLoadedIsHarmless()
    {
        var id = NewProviderId();

        SearchableItemCache.Invalidate(id);

        Assert.IsFalse(SearchableItemCache.TryGetEntries(id, out _));
        Assert.IsFalse(SearchableItemCache.IsStale(id));
    }

    [TestMethod]
    public void CacheEntry_SnapshotsCleansAndBoundsProviderAliases()
    {
        var source = new List<SearchableItemAlias>
        {
            new("  PixPin  ", SearchableItemAliasKind.ShortcutTarget),
            new("pixpin", SearchableItemAliasKind.ProductMetadata),
            new("   ", SearchableItemAliasKind.Curated),
            new(new string('x', 129), SearchableItemAliasKind.ShortcutTarget),
        };
        source.AddRange(Enumerable.Range(0, 40)
            .Select(index => new SearchableItemAlias($"alias-{index}", SearchableItemAliasKind.Curated)));
        var entry = new SearchableItemCache.CacheEntry(
            new SearchableItem { Title = "截图工具", Aliases = source },
            new List<string>(),
            null);

        source[0] = new SearchableItemAlias("mutated", SearchableItemAliasKind.ShortcutTarget);

        Assert.HasCount(32, entry.ProviderAliases);
        Assert.AreEqual("PixPin", entry.ProviderAliases[0].Text);
        Assert.AreEqual(SearchableItemAliasKind.ShortcutTarget, entry.ProviderAliases[0].Kind);
        Assert.IsFalse(entry.ProviderAliases.Any(alias => alias.Text == "mutated"));
    }

    [TestMethod]
    public void DisplayTitleMatch_IsNotOverriddenByHigherCoverageSecondaryAlias()
    {
        // "guohe" covers 5/12 of the title but 5/9 of the alias: comparing only coverage used to let the
        // alias replace a direct title hit, so the row lost its DirectStrong evidence and fell behind
        // every other direct match. The title must win whenever it matches at all.
        var entry = new SearchableItemCache.CacheEntry(
            new SearchableItem
            {
                Title = "guohe 果核看图纸",
                Aliases =
                [
                    new SearchableItemAlias("GuoheView", SearchableItemAliasKind.ShortcutTarget),
                ],
            },
            new List<string>(),
            null);

        var match = SearchableItemMapper.MatchCatalogEntryWithProvenance(
            FuzzyQuery.Parse("guohe"),
            ["guohe"],
            entry,
            "StartMenuAppItemProvider");

        Assert.IsTrue(match.Result.Rank.IsMatch);
        Assert.AreEqual(MatchEvidenceField.DisplayTitle, match.Result.Evidence.Field);
        Assert.AreEqual(MatchEvidenceGrade.DirectStrong, match.Result.Evidence.Grade);
        Assert.IsNull(match.MatchedSecondaryText);
    }

    [TestMethod]
    public void ShadowAliasMatch_RecordsProviderVariantAndProvenanceField()
    {
        var entry = new SearchableItemCache.CacheEntry(
            new SearchableItem
            {
                Title = "截图工具",
                Aliases =
                [
                    new SearchableItemAlias("PixPin", SearchableItemAliasKind.ShortcutTarget),
                ],
            },
            new List<string>(),
            null);

        var query = FuzzyQuery.Parse("pixpin");
        var production = SearchableItemMapper.MatchCatalogEntryWithEvidence(
            query,
            ["pixpin"],
            entry,
            "StartMenuAppItemProvider");
        var match = SearchableItemMapper.MatchShadowAliases(
            query,
            entry,
            "StartMenuAppItemProvider");

        Assert.IsTrue(production.Rank.IsMatch, "clean V3 secondary aliases participate with explicit SecondaryField grade");
        Assert.AreEqual(MatchEvidenceField.ShortcutTarget, production.Evidence.Field);
        Assert.AreEqual(MatchEvidenceGrade.SecondaryField, production.Evidence.Grade);
        Assert.IsTrue(match.Rank.IsMatch);
        Assert.AreEqual(MatchEvidenceField.ShortcutTarget, match.Evidence.Field);
        Assert.AreEqual(MatchQueryVariant.ProviderAlias, match.Evidence.QueryVariant);
        Assert.AreEqual(MatchEvidenceGrade.SecondaryField, match.Evidence.Grade);
        Assert.AreEqual("StartMenuAppItemProvider", match.Evidence.ProviderId);
    }

    [TestMethod]
    public void SecondaryAliasGate_RejectsScatteredSubsequences()
    {
        var entry = new SearchableItemCache.CacheEntry(
            new SearchableItem
            {
                Title = "Telegram",
                ResultKind = "Application",
                ActionArgument = @"X:\Apps\Telegram.lnk",
                Aliases = [new SearchableItemAlias("r-e-a-d-y-z-b-l", SearchableItemAliasKind.ProductMetadata)],
            },
            new List<string>(),
            null);

        var scattered = SearchableItemMapper.MatchCatalogEntryWithProvenance(
            FuzzyQuery.Parse("zbl"),
            ["zbl"],
            entry,
            "StartMenuAppItemProvider");
        Assert.IsFalse(scattered.Result.Rank.IsMatch, "#440 scattered metadata subsequences must stay excluded");
    }

    [TestMethod]
    public void TrustedAliasShadowRecall_ControlledCasesIsComplete()
    {
        var cases = new[]
        {
            (Query: "pixpin", Text: "PixPin", Kind: SearchableItemAliasKind.ShortcutTarget,
                Field: MatchEvidenceField.ShortcutTarget),
            (Query: "screen capture", Text: "Screen Capture", Kind: SearchableItemAliasKind.ProductMetadata,
                Field: MatchEvidenceField.ProductMetadata),
            (Query: "windowscalculator", Text: "WindowsCalculator", Kind: SearchableItemAliasKind.ProductMetadata,
                Field: MatchEvidenceField.ProductMetadata),
            (Query: "telegram", Text: "Telegram", Kind: SearchableItemAliasKind.Curated,
                Field: MatchEvidenceField.CuratedAlias),
        };
        var recalled = 0;
        foreach (var item in cases)
        {
            var entry = new SearchableItemCache.CacheEntry(
                new SearchableItem
                {
                    Title = "本地化标题",
                    Aliases = [new SearchableItemAlias(item.Text, item.Kind)],
                },
                new List<string>(),
                null);
            var match = SearchableItemMapper.MatchShadowAliases(
                FuzzyQuery.Parse(item.Query), entry, "fixture-provider");
            if (match.Rank.IsMatch)
                recalled++;
            Assert.AreEqual(item.Field, match.Evidence.Field, item.Query);
            Assert.AreEqual(MatchEvidenceGrade.SecondaryField, match.Evidence.Grade, item.Query);
        }

        Assert.AreEqual(cases.Length, recalled, "controlled trusted-alias shadow Recall must be 100%");
    }

    [TestMethod]
    public void ReadinessSnapshot_DistinguishesReadyWithHitsFromStale()
    {
        var provider = new ReadinessProvider();
        var id = provider.GetType().Name;
        SearchableItemCache.Seed(id,
        [
            new SearchableItemCache.CacheEntry(
                new SearchableItem { Title = "App" },
                new List<string>(),
                null)
        ]);

        var ready = SearchableItemCache.ReadinessSnapshot([provider],
            new Dictionary<string, int> { [id] = 1 }).Single();
        Assert.AreEqual(RankingProviderState.ReadyWithItems, ready.State);
        Assert.AreEqual(1, ready.ItemCount);
        Assert.AreEqual(1, ready.HitCount);

        SearchableItemCache.Invalidate(id);
        var stale = SearchableItemCache.ReadinessSnapshot([provider]).Single();
        Assert.AreEqual(RankingProviderState.Stale, stale.State);
    }

    private sealed class ReadinessProvider : ISearchableItemProvider
    {
        public event Action? ItemsChanged;
        public IEnumerable<SearchableItem> GetSearchableItems() => Array.Empty<SearchableItem>();
    }
}
