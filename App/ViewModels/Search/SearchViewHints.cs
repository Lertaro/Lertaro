using System.ComponentModel;
using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Search;

// The empty-query / no-results hints the full search window shows over its result area, computed from state
// the view model already exposes.
//
// Composed into SearchViewModel rather than inlined there purely to keep that file under the repository's
// per-file line limit -- the same reason the query-token dispatch and result rendering already live in their
// own classes. It is deliberately a class the view model exposes rather than a set of extension methods:
// these are WPF binding TARGETS, and a binding cannot reach an extension method.
//
// Every member a binding path names here is PUBLIC, and so is the SearchViewModel.Hints property that
// reaches them. That is load-bearing rather than stylistic: WPF resolves a binding path through a
// public-only reflection lookup, so a non-public property is silently skipped -- no error, no log, the
// trigger and the text simply keep their defaults. The TYPE has to be public as well, because C# rejects a
// public property whose type is less accessible (CS0053); public in an application assembly costs nothing.
//
// It raises its own PropertyChanged for all three hints on Refresh rather than leaving that to the view
// model: all three read the same state (the query, the result count, actions mode), so the view model only
// has to say "something they watch moved" in one call instead of naming three properties it no longer owns.
public sealed class SearchViewHints(SearchViewModel viewModel) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The welcome line, shown before anything is typed.</summary>
    public bool ShowWelcomeHint => !viewModel.IsActionsMode && string.IsNullOrWhiteSpace(viewModel.AdvancedQuery);

    /// <summary>
    /// The generic "no results" line: a query was typed and nothing matched. False while a search is
    /// still running -- an empty list then means "nothing has arrived yet", and showing this line is what
    /// made the full window read as blank (or as having found nothing) during the seconds its first paint
    /// can take. <see cref="SearchViewModel.IsSearching"/> re-raises this through Refresh.
    /// </summary>
    public bool ShowNoResultsHint => !viewModel.IsActionsMode
        && !viewModel.IsSearching
        && viewModel.FilteredResults.Count == 0
        && !string.IsNullOrWhiteSpace(viewModel.AdvancedQuery);

    /// <summary>
    /// A regex clause the engine could not compile matches nothing, so a query mixing one with ordinary
    /// words returns nothing at all with no clue as to which part was at fault -- while the generic "no
    /// results" line reads as "your search is too narrow", the opposite of the truth. This names the clause
    /// instead. Null unless the failures are the reason nothing is on screen, which is exactly the state
    /// <see cref="ShowNoResultsHint"/> describes.
    /// </summary>
    public string? InvalidRegexHint
    {
        get
        {
            if (!ShowNoResultsHint)
                return null;

            // Asked of the query being displayed rather than read out of shared state, so the answer cannot
            // be stale (a search the user has already typed past), missing (a clause the compile cache
            // answered without re-reporting), or empty for the common case (a clause compiled in the
            // service process, whose report never came back).
            var clean = Core.SearchIndex.Query.QueryTokenScanner.Scan(
                Core.SearchIndex.Query.QueryTokenScanner.StripExclusionBypass(viewModel.AdvancedQuery, out _),
                Helpers.GlobalTokenPrefix.Current).Text;
            var invalid = SearchContext.UncompilableClauses(clean);
            if (invalid.Count == 0)
                return null;

            return string.Format(
                TranslationManager.Instance["Search_InvalidRegex"],
                string.Join(" ", invalid.Select(p => $"/{p}/")));
        }
    }

    /// <summary>Tells every binding to re-read. See the class comment for why all three move together.</summary>
    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowNoResultsHint)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowWelcomeHint)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InvalidRegexHint)));
    }
}
