namespace Lertaro.App.ViewModels.Settings.QuickPanel;

/// <summary>One plugin-provided tab, and whether the strip currently shows it.</summary>
/// <remarks>
/// Deliberately not the same question as the plugin page's own enable/disable, which governs whether the
/// provider is loaded at all. This one is only about the strip: a tab closed with its x is unticked
/// here, and ticking it puts the tab back.
///
/// Ticked by default, unlike a folder, which the user goes and adds: this tab is there because a plugin
/// they installed offers it.
/// </remarks>
public sealed class QuickPanelPluginTabOption : ViewModelBase
{
    public QuickPanelPluginTabOption(string id, Func<string> name, bool isOpen, bool showAsList)
    {
        Id = id;
        _name = name;
        _isOpen = isOpen;
        _showAsList = showAsList;
    }

    public string Id { get; }

    private readonly Func<string> _name;

    /// <summary>What the plugin calls this tab, re-asked every read rather than snapshotted.</summary>
    /// <remarks>
    /// A provider builds its Name from its own TranslationService.Get call, so the string is only correct
    /// for the language that was active when it was read: held as a string, this row went on showing the
    /// old language after a switch while the page around it changed. Same reason ColumnOrderItem takes a
    /// Func for its header.
    /// </remarks>
    public string Name => _name();

    /// <summary>Re-reads <see cref="Name"/> after a language switch.</summary>
    public void NotifyLanguageChanged() => OnPropertyChanged(nameof(Name));

    private bool _isOpen;

    public bool IsOpen
    {
        get => _isOpen;
        set => SetProperty(ref _isOpen, value);
    }

    private bool _showAsList;

    /// <summary>The tab opens as a detail list rather than as thumbnail tiles.</summary>
    /// <remarks>
    /// Off by default, tiles being what a panel of files is for. The same choice a folder source has,
    /// asked here because a plugin tab has no row in a workspace's source list to ask it on. What the
    /// header's own toggle does in the panel still overrides this for as long as it is open.
    /// </remarks>
    public bool ShowAsList
    {
        get => _showAsList;
        set => SetProperty(ref _showAsList, value);
    }
}
