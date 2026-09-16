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
// It raises its own PropertyChanged for all three hints on Refresh rather than leaving that to the view
// model: all three read the same state (the query, the result count, actions mode), so the view model only
// has to say "something they watch moved" in one call instead of naming three properties it no longer owns.
internal sealed class SearchViewHints(SearchViewModel viewModel) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The welcome line, shown before anything is typed.</summary>
    public bool ShowWelcomeHint => !viewModel.IsActionsMode && string.IsNullOrWhiteSpace(viewModel.AdvancedQuery);

    /// <summary>The generic "no results" line: a query was typed and nothing matched.</summary>
    public bool ShowNoResultsHint => !viewModel.IsActionsMode
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
            if (!ShowNoResultsHint || SearchContext.InvalidRegexes.Count == 0)
                return null;

            return string.Format(
                TranslationManager.Instance["Search_InvalidRegex"],
                string.Join(" ", SearchContext.InvalidRegexes.Select(p => $"/{p}/")));
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
