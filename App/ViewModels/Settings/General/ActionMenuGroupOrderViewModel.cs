using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.Services.ShellMenu.ActionFlyout;
using Lertaro.Core;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.ViewModels.Settings.General;

// Lets the user reorder the Actions menu's own top-level sections -- the built-in group plus one
// entry per IDynamicActionProvider (e.g. Custom Actions) -- mirroring SidebarGroupOrderViewModel's
// pattern. Listed here without any selection context (unlike the live menu, which filters groups by
// CanExecute/CanProvide against the current selection), since this is a global ordering preference,
// not tied to whatever file happens to be selected right now.
public class ActionMenuGroupOrderViewModel : ViewModelBase
{
    private readonly System.ComponentModel.PropertyChangedEventHandler _translationHandler;

    private readonly UserSettings _userSettings;

    public ActionMenuGroupOrderViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        // Re-read fresh on every call (not captured once into a local) so a later language switch's
        // NotifyLanguageChanged re-invocation of DisplayName picks up the new translation too.
        static string BuiltinLabel() => TranslationManager.Instance["Action_BuiltinGroup"];

        var builtinLabel = BuiltinLabel();
        var seenIds = new HashSet<string>();
        var candidates = new List<ActionMenuGroupOrderItem>();

        foreach (var registration in PluginManager.Instance.Actions)
        {
            var group = string.IsNullOrWhiteSpace(registration.Action.GroupName) ? builtinLabel : registration.Action.GroupName;
            var id = ActionMenuBuilder.BuildStaticGroupId(group, builtinLabel);
            if (!seenIds.Add(id)) continue;
            var action = registration.Action;
            candidates.Add(new ActionMenuGroupOrderItem(id, () => string.IsNullOrWhiteSpace(action.GroupName) ? BuiltinLabel() : action.GroupName));
        }

        // Same Priority-ascending order ActionMenuBuilder.BuildDynamic itself renders with, so this
        // list's un-reordered starting position matches what the user would actually see live.
        foreach (var provider in PluginManager.Instance.DynamicActionProviders.OrderBy(p => p.Priority))
        {
            var group = string.IsNullOrWhiteSpace(provider.GroupName) ? builtinLabel : provider.GroupName;
            var id = ActionMenuBuilder.BuildDynamicGroupId(provider);
            if (!seenIds.Add(id)) continue;
            candidates.Add(new ActionMenuGroupOrderItem(id, () => string.IsNullOrWhiteSpace(provider.GroupName) ? BuiltinLabel() : provider.GroupName));
        }

        var order = userSettings.ActionMenuGroupOrder;
        foreach (var item in candidates.OrderBy(c =>
        {
            var rank = order.IndexOf(c.Id);
            return rank >= 0 ? rank : int.MaxValue;
        }))
        {
            Items.Add(item);
        }

        MoveUpCommand = new RelayCommand<ActionMenuGroupOrderItem>(MoveUp);
        MoveDownCommand = new RelayCommand<ActionMenuGroupOrderItem>(MoveDown);

        _translationHandler = (_, _) =>
        {
            foreach (var item in Items)
                item.NotifyLanguageChanged();
        };
        TranslationManager.Instance.PropertyChanged += _translationHandler;

    }

    public ObservableCollection<ActionMenuGroupOrderItem> Items { get; } = new();

    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    private void MoveUp(ActionMenuGroupOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx > 0) Items.Move(idx, idx - 1);
    }

    private void MoveDown(ActionMenuGroupOrderItem? item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx >= 0 && idx < Items.Count - 1) Items.Move(idx, idx + 1);
    }

    public void Save() => _userSettings.ActionMenuGroupOrder = Items.Select(x => x.Id).ToList();

    public void Cleanup() => TranslationManager.Instance.PropertyChanged -= _translationHandler;
}

public class ActionMenuGroupOrderItem : OrderItemBase
{
    public ActionMenuGroupOrderItem(string id, Func<string> resolveDisplayName) : base(id, resolveDisplayName) { }
}
