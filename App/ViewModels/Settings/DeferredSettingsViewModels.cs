using Lertaro.App.ViewModels.Settings.General;
using Lertaro.Core;
using Lertaro.Core.Services.Search;

namespace Lertaro.App.ViewModels.Settings;

/// <summary>
/// The Settings window's three DEFERRED sub-view-models -- the ones whose constructors do real work -- plus
/// the guard that keeps that deferral honest.
/// </summary>
/// <remarks>
/// Split out of <see cref="SettingsViewModel"/> to keep that file under the repo's per-file line limit, and
/// because these three share one concern that the others do not: they are the only sub-VMs here whose
/// construction is expensive enough to be worth deferring. This class holds a reference to the owning view
/// model and is called through by its properties; it is composition, not inheritance.
///
/// Deferring them is the other half of issue #186. That fixed the PAGES (each settings page is built only on
/// first visit), but <see cref="SettingsViewModel"/>'s constructor still built every sub-VM eagerly, so
/// opening the window paid for all of this anyway -- which is why every tab felt sluggish from the start
/// rather than only the first one. Costs being deferred:
///
///   * Log        -- reads and parses up to 500 lines of app.log.
///   * Appearance -- enumerates every theme provider three times (normal/light/dark), and each provider
///                   constructs a WPF ResourceDictionary per theme (~48 themes in a release build).
///   * History    -- both lists load and map their entries in their constructors, reading
///                   search-history.json (or its backup) and up to 2000 lines of keyword-history.txt.
/// </remarks>
internal sealed class DeferredSettingsViewModels
{
    private readonly UserSettings _userSettings;
    private readonly SearchService _searchService;

    private ServiceLogViewModel? _log;
    private ThemeSettingsViewModel? _appearance;
    private HistorySettingsViewModel? _history;

    internal DeferredSettingsViewModels(UserSettings userSettings, SearchService searchService)
    {
        _userSettings = userSettings;
        _searchService = searchService;
    }

    internal ServiceLogViewModel Log => _log ??= new ServiceLogViewModel(_searchService);
    internal ThemeSettingsViewModel Appearance => _appearance ??= new ThemeSettingsViewModel(_userSettings);
    internal HistorySettingsViewModel History => _history ??= new HistorySettingsViewModel(_userSettings);

    /// <summary>
    /// Whether each of these has been built, for the owner's own non-user touch points -- saving on Apply,
    /// disposing on close -- which must not construct one just to talk to it.
    /// </summary>
    internal ServiceLogViewModel? ExistingLog => _log;
    internal ThemeSettingsViewModel? ExistingAppearance => _appearance;
    internal HistorySettingsViewModel? ExistingHistory => _history;
}
