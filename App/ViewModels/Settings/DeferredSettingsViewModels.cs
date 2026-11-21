using Lertaro.App.ViewModels.Settings.General;
using Lertaro.Core;
using Lertaro.Core.Services.Search;

namespace Lertaro.App.ViewModels.Settings;

/// <summary>
/// Creates expensive Settings sub-view-models on first use: Log reads up to 500 log lines; Appearance
/// enumerates normal/light/dark theme resources; History loads search records and up to 2,000
/// keyword-history lines. Deferring pages alone does not defer these constructor costs.
/// </summary>
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
