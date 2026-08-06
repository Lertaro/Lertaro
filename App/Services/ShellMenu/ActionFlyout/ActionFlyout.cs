using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.Core;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using MenuItem = System.Windows.Controls.MenuItem;
using Border = System.Windows.Controls.Border;
using StackPanel = System.Windows.Controls.StackPanel;
using Application = System.Windows.Application;
using ItemsPanelTemplate = System.Windows.Controls.ItemsPanelTemplate;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using KeyEventHandler = System.Windows.Input.KeyEventHandler;
using MouseButtonEventHandler = System.Windows.Input.MouseButtonEventHandler;
using Lertaro.App.Services.ShellMenu.QuickNav.RightClickActions;
namespace Lertaro.App.Services.ShellMenu.ActionFlyout;

/// <summary>
/// The full-window action menu, shown as a native-style flyout popup instead of the in-window actions
/// panel. Content and execution come from the shared <see cref="ActionFlyoutItems"/> (same as the
/// quick-nav right-click menu); this class only owns the popup placement, keyboard nav and close logic.
/// </summary>
public static class ActionFlyout
{
    private const SearchWindowType MenuWindowType = SearchWindowType.Main;

    // True while a flyout is open, so the owner window's own key handler stands down and lets the flyout
    // navigate (arrows/enter/escape) instead of moving the result selection behind it.
    public static bool IsOpen { get; private set; }

    private static Popup? _popup;
    private static Window? _owner;
    private static KeyEventHandler? _keyHandler;
    private static MouseButtonEventHandler? _mouseDownHandler;
    private static EventHandler? _deactivatedHandler;
    private static int _generation;

    public static void Show(
        IReadOnlyList<AppSearchResult> selection,
        IPluginSearchWindow view,
        Window ownerWindow,
        UIElement anchor,
        PlacementMode placement)
    {
        Close();
        if (selection == null || selection.Count == 0) return;

        var generation = ++_generation;
        var cmdMap = new Dictionary<uint, IDynamicActionProvider>();
        var subMap = new Dictionary<IntPtr, IDynamicActionProvider>();

        // Build the shell group off the UI thread (2s cap), then the built-in actions on the UI thread,
        // and show once ready — a slow shell extension never freezes the window.
        _ = Task.Run(async () =>
        {
            // Spans the whole build, the UI-thread half included, so every action's own
            // File.Exists/Directory.Exists gate is answered from one pass over the selection instead of
            // one pass each. Primed here rather than left to the first action that asks, so the probing
            // happens on this thread and not on the UI thread where BuildStatic runs. Prime stops at the
            // first missing path exactly as those All() gates do, so this is never more work than before.
            using var existence = PathExistenceCache.BeginScope();
            PathExistenceCache.Prime(selection.Select(r => r.FullPath));

            List<ActionMenuItem>? dynamicItems = null;
            try
            {
                var buildTask = Task.Run(() => ActionMenuBuilder.BuildDynamic(selection, IntPtr.Zero, MenuWindowType, cmdMap, subMap));
                if (buildTask.Wait(2000))
                    dynamicItems = buildTask.Result;
                else
                    Logger.Log("[ActionFlyout] Shell menu build exceeded 2s; showing built-in actions only.", LogLevel.Warn);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ActionFlyout] Shell menu build failed: {ex.Message}", LogLevel.Error);
            }

            // Awaited, not fired and forgotten: the scope above has to outlive BuildStatic, which runs
            // inside this callback on the UI thread.
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (generation != _generation || !ownerWindow.IsVisible)
                    return;

                var merged = ActionMenuBuilder.BuildStatic(selection, MenuWindowType);
                if (dynamicItems != null) merged.AddRange(dynamicItems);
                var finalItems = ActionMenuBuilder.FinalizeItems(merged);
                if (finalItems.Count == 0) return;

                BuildAndShow(finalItems, selection, cmdMap, subMap, view, ownerWindow, anchor, placement);
            }).Task.ConfigureAwait(false);
        });
    }

    private static void BuildAndShow(
        List<ActionMenuItem> finalItems,
        IReadOnlyList<AppSearchResult> selection,
        Dictionary<uint, IDynamicActionProvider> cmdMap,
        Dictionary<IntPtr, IDynamicActionProvider> subMap,
        IPluginSearchWindow view,
        Window ownerWindow,
        UIElement anchor,
        PlacementMode placement)
    {
        var menu = new System.Windows.Controls.Menu
        {
            Background = System.Windows.Media.Brushes.Transparent,
            Focusable = false
        };
        var template = new ItemsPanelTemplate();
        var factory = new FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(System.Windows.Controls.Panel.IsItemsHostProperty, true);
        template.VisualTree = factory;
        menu.ItemsPanel = template;

        var flyoutStyle = (Style)Application.Current.FindResource("ActionFlyoutMenuItemStyle");
        var menuItems = ActionFlyoutItems.PopulateMenu(
            menu, finalItems, selection, cmdMap, subMap, MenuWindowType, view, flyoutStyle, closeFlyout: Close);

        if (menuItems.Count == 0) return;

        var border = new Border
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            Child = menu
        };
        border.SetResourceReference(Border.BackgroundProperty, "MenuBackground");
        border.SetResourceReference(Border.BorderBrushProperty, "MenuBorderBrush");
        border.SetResourceReference(Border.CornerRadiusProperty, "CornerRadiusPopover");
        border.SetResourceReference(UIElement.EffectProperty, "Elevation1");

        // anchor must be an element that outlives the flyout. A Popup goes away with its PlacementTarget,
        // so anchoring to something WPF can recycle -- a virtualized list's row container being the trap
        // here -- makes the flyout close by itself the moment that happens, with nothing about the
        // selection or the window having changed. See SearchWindowInputHandler.ShowActionFlyout.
        _popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = placement,
            AllowsTransparency = true,
            StaysOpen = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = border
        };
        _owner = ownerWindow;

        // No row is pre-selected (unlike the in-window list); the first Down/Up highlights first/last.
        var isHighlightedKey = ActionFlyoutItems.IsHighlightedKey;

        _keyHandler = (s, e) => HandleKey(e, menu, isHighlightedKey);
        ownerWindow.AddHandler(UIElement.PreviewKeyDownEvent, _keyHandler, handledEventsToo: true);

        // A mouse-down reaching the owner window (popups are a separate visual tree, so item clicks do
        // not) means the user clicked outside the flyout — close it.
        _mouseDownHandler = (s, e) => Close();
        ownerWindow.AddHandler(UIElement.PreviewMouseDownEvent, _mouseDownHandler, handledEventsToo: true);

        _deactivatedHandler = (s, e) => Close();
        ownerWindow.Deactivated += _deactivatedHandler;

        IsOpen = true;
        _popup.IsOpen = true;
    }

    private static void HandleKey(KeyEventArgs e, System.Windows.Controls.Menu menu, DependencyPropertyKey? isHighlightedKey)
    {
        if (_popup is not { IsOpen: true } || isHighlightedKey == null) return;

        var state = PluginContextMenuBuilder.GetActiveMenuState(menu, isHighlightedKey);
        if (state.items.Count == 0)
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            return;
        }

        void UpdateHighlight(int newIdx)
        {
            if (state.highlightedIndex >= 0 && state.highlightedIndex < state.items.Count)
                state.items[state.highlightedIndex].SetValue(isHighlightedKey, false);
            if (newIdx >= 0 && newIdx < state.items.Count)
            {
                state.items[newIdx].SetValue(isHighlightedKey, true);
                state.items[newIdx].BringIntoView();
            }
        }

        // The user's configurable next/previous-item hotkeys should move the highlight here too, not
        // just the literal arrow keys -- otherwise a custom binding silently stops working once this
        // flyout (or one of its nested submenus) is open.
        var actualKey = WpfUiHelper.GetActualKey(e);
        var hotkeys = UserSettings.Load().Hotkeys;
        var effectiveKey = e.Key;
        if (WpfUiHelper.MatchesHotkey(hotkeys.NextItemHotkey, Keyboard.Modifiers, actualKey))
            effectiveKey = Key.Down;
        else if (WpfUiHelper.MatchesHotkey(hotkeys.PreviousItemHotkey, Keyboard.Modifiers, actualKey))
            effectiveKey = Key.Up;

        switch (effectiveKey)
        {
            case Key.Down:
                e.Handled = true;
                UpdateHighlight(state.highlightedIndex < 0 ? 0 : (state.highlightedIndex + 1) % state.items.Count);
                break;
            case Key.Up:
                e.Handled = true;
                UpdateHighlight(state.highlightedIndex <= 0 ? state.items.Count - 1 : state.highlightedIndex - 1);
                break;
            case Key.Right:
                {
                    var active = state.highlightedIndex >= 0 && state.highlightedIndex < state.items.Count ? state.items[state.highlightedIndex] : null;
                    if (active != null && active.HasItems)
                    {
                        e.Handled = true;
                        active.IsSubmenuOpen = true;
                        var first = active.Items.OfType<MenuItem>().FirstOrDefault(mi => mi.IsEnabled);
                        first?.SetValue(isHighlightedKey, true);
                    }
                    break;
                }
            case Key.Left:
                if (state.parent is MenuItem parentMenuItem)
                {
                    e.Handled = true;
                    parentMenuItem.IsSubmenuOpen = false;
                }
                break;
            case Key.Escape:
                e.Handled = true;
                Close();
                break;
            case Key.Enter:
            case Key.Space:
                if (state.highlightedIndex >= 0)
                {
                    var active = state.items[state.highlightedIndex];
                    e.Handled = true;
                    if (active.HasItems)
                    {
                        active.IsSubmenuOpen = true;
                        var first = active.Items.OfType<MenuItem>().FirstOrDefault(mi => mi.IsEnabled);
                        first?.SetValue(isHighlightedKey, true);
                    }
                    else
                    {
                        active.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    }
                }
                break;
        }
    }

    public static void Close()
    {
        if (_popup == null)
        {
            IsOpen = false;
            return;
        }

        if (_owner != null)
        {
            if (_keyHandler != null) _owner.RemoveHandler(UIElement.PreviewKeyDownEvent, _keyHandler);
            if (_mouseDownHandler != null) _owner.RemoveHandler(UIElement.PreviewMouseDownEvent, _mouseDownHandler);
            if (_deactivatedHandler != null) _owner.Deactivated -= _deactivatedHandler;
        }

        _popup.IsOpen = false;
        _popup = null;
        _owner = null;
        _keyHandler = null;
        _mouseDownHandler = null;
        _deactivatedHandler = null;
        IsOpen = false;
    }
}
