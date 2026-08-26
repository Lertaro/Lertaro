using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Services;
using Imaging = System.Windows.Interop.Imaging;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Image = System.Windows.Controls.Image;
using Separator = System.Windows.Controls.Separator;
using Application = System.Windows.Application;
using MouseButtonEventHandler = System.Windows.Input.MouseButtonEventHandler;

using Lertaro.App.Services.ShellIcons;
using Lertaro.App.Services.ShellMenu.QuickNav.RightClickActions;
using Lertaro.App.Converters;
namespace Lertaro.App.Services.ShellMenu.QuickNav;

// Menu-item construction for QuickNavigationMenu, split out (composition, not a partial class) to keep
// QuickNavigationMenu.cs under the project's 300-line limit. QuickNavigationMenu itself keeps Show (the
// top-level entry point) and FindVisualParent (referenced by name from outside this file, e.g.
// RightClickActions/PluginContextMenuHelper.cs and its own tests) plus thin forwarders for CreateSeparator
// and CreateMenuItem, since QuickNavigationSubMenuLoader calls those two directly by that name.
internal static class QuickNavigationMenuContentExtensions
{
    // Explicit SeparatorBrush reference (SetResourceReference, not a plain Style lookup) rather than a
    // bare `new Separator()`: this popup's items are built entirely in code with no local Style set, so
    // it was left depending on Menu.xaml's implicit TargetType="Separator" style resolving correctly for
    // an ad-hoc ContextMenu -- it visually came out a noticeably different, more saturated color than
    // the actions menu's own separator, which uses SeparatorBrush directly rather than through implicit
    // style matching. Forcing the same resource here, the same way ActionFlyout.cs already does for its
    // own code-built popup chrome (SetResourceReference, so it still follows live theme switching),
    // guarantees the two actually match instead of relying on two different resolution paths agreeing.
    internal static Separator CreateSeparator()
    {
        var separator = new Separator();
        separator.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "SeparatorBrush");
        return separator;
    }

    // One per IQuickNavigationProvider contributing root-level items, labeling which provider they came
    // from -- same non-interactive, always-shown-even-for-a-single-group convention the actions menu's
    // own section headers use (ActionMenuItemTemplate's SectionHeaderVisibility block), via a dedicated
    // style (QuickNavGroupHeaderStyle in Menu.xaml) since this popup's items are plain MenuItems, not
    // ActionMenuItemTemplate-driven rows.
    internal static MenuItem CreateGroupHeader(string groupName, Action? headerAction, string? headerActionTooltip, ContextMenu contextMenu) =>
        CreateHeaderMenuItem(groupName, headerAction, headerActionTooltip, contextMenu);

    // Shared by the root-level provider group header (Show()) and any DynamicMenuItem a provider marks
    // IsHeader (CreateMenuItem below), so a submenu category header looks and behaves identically to
    // the root one. Header set to a plain string with no action, or a Grid (label + button) once one
    // is wired -- QuickNavGroupHeaderStyle's TemplateBinding only works for the former, hence the two
    // separate styles in Menu.xaml.
    private static MenuItem CreateHeaderMenuItem(string text, Action? headerAction, string? headerActionTooltip, ContextMenu contextMenu)
    {
        if (headerAction == null)
        {
            return new MenuItem
            {
                Header = text,
                Style = (Style)Application.Current.FindResource("QuickNavGroupHeaderStyle")
            };
        }

        var grid = new System.Windows.Controls.Grid();
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

        var textBlock = new System.Windows.Controls.TextBlock
        {
            Text = text,
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("AccentBlue"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        System.Windows.Controls.Grid.SetColumn(textBlock, 0);
        grid.Children.Add(textBlock);

        var button = new System.Windows.Controls.Button
        {
            Content = "\uE710", // Segoe MDL2 Assets "Add" glyph
            Style = (Style)Application.Current.FindResource("QuickNavHeaderActionButtonStyle"),
            ToolTip = headerActionTooltip
        };
        System.Windows.Controls.Grid.SetColumn(button, 1);
        // Closes the menu and runs the action the same way triggerAction below does for any normal
        // leaf item's OnExecute -- including the PlacementTarget.Hide() call, which turned out to
        // matter here: without it, the prompt window PluginFieldPromptWindow.ShowInternal opens could
        // still pick the popup's own (by-then-closing) helper window as its Owner, and WPF closes a
        // window's owned windows right along with it -- the new prompt would flash open and
        // immediately close again. Deferred to Background so the menu is fully gone by the time the
        // action's own UI shows, matching every other item's click handling.
        button.Click += (s, e) =>
        {
            e.Handled = true;
            contextMenu.IsOpen = false;
            (contextMenu.PlacementTarget as Window)?.Hide();
            Application.Current.Dispatcher.BeginInvoke(headerAction, System.Windows.Threading.DispatcherPriority.Background);
        };
        grid.Children.Add(button);

        return new MenuItem
        {
            Header = grid,
            Focusable = false,
            // A click anywhere on this row (not just the button) would otherwise close the whole menu
            // per WPF's default leaf-MenuItem behavior, even though nothing was actually invoked --
            // this keeps the row itself inert; the button's own handler above closes the menu on its
            // own terms once headerAction has actually been dispatched.
            StaysOpenOnClick = true,
            Style = (Style)Application.Current.FindResource("QuickNavGroupHeaderWithActionStyle")
        };
    }

    // Caps how wide a single cascading-menu row's own text is allowed to grow before it starts
    // scrolling instead -- without this, a long file/folder name (see GitHub issue #184: a level-3
    // Explorer folder with a long name) made the whole ContextMenu/Popup auto-size to fit it, dragging
    // every OTHER row's column out just as wide. Chosen to comfortably fit a typical folder name at this
    // menu's own FontSize (12.5, see Menu.xaml) without still feeling like a Listary-style cramped fixed
    // width -- not user-configurable, matching how every other row metric in this menu is a fixed value.
    private const double MaxItemTextWidth = 220;

    // ScrollViewer clips the TextBlock to MaxItemTextWidth without a visible scrollbar -- the same
    // container shape DataTemplates.xaml's own search-result rows already use for MarqueeBehavior, just
    // built in code here since this menu has no XAML template of its own. TextTrimming stays None: the
    // marquee is what reveals the rest of an overflowing name (on hover/keyboard-highlight), not an
    // ellipsis, which would defeat the point of scrolling to it at all.
    internal static System.Windows.Controls.ScrollViewer CreateItemHeader(string text)
    {
        var textBlock = new System.Windows.Controls.TextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.None,
            VerticalAlignment = VerticalAlignment.Center
        };
        Helpers.Visuals.MarqueeBehavior.SetEnableMarquee(textBlock, true);

        var header = new System.Windows.Controls.ScrollViewer
        {
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
            Focusable = false,
            MaxWidth = MaxItemTextWidth,
            Content = textBlock
        };
        ScrollViewerHelper.SetBubbleMouseWheel(header, true);
        return header;
    }

    internal static MenuItem CreateMenuItem(DynamicMenuItem item, ISearchResult result, IQuickNavigationProvider provider, ContextMenu contextMenu, QuickNavTriggerContext trigger, bool enableRightClick = true, bool isRootItem = false)
    {
        if (item.IsHeader)
        {
            return CreateHeaderMenuItem(item.Text, item.OnExecute, null, contextMenu);
        }

        var menuItem = new MenuItem { Header = CreateItemHeader(item.Text), IsEnabled = !item.IsDisabled, Focusable = !item.IsDisabled };

        if (item.HBitmapItem != IntPtr.Zero)
        {
            try
            {
                menuItem.Icon = new Image
                {
                    Source = Imaging.CreateBitmapSourceFromHBitmap(
                        item.HBitmapItem,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions())
                };
            }
            catch { }
        }

        string? itemPath = null;
        if (item.HasSubMenu && item.SubMenuHandle != IntPtr.Zero)
        {
            itemPath = QuickNavigationPathResolver.TryResolveSubMenuPath(provider, item.SubMenuHandle);
            menuItem.Items.Add(new MenuItem { Header = TranslationService.Get("QuickNav_Loading"), IsEnabled = false });
            menuItem.GotKeyboardFocus += (s, e) =>
            {
                QuickNavigationSubMenuLoader.EnsureLoaded(menuItem, result, item, provider, contextMenu, trigger);
                Application.Current.Dispatcher.BeginInvoke(new Action(() => { if (menuItem.IsKeyboardFocusWithin || menuItem.IsFocused) menuItem.IsSubmenuOpen = true; }));
            };
            menuItem.MouseEnter += (s, e) => QuickNavigationSubMenuLoader.EnsureLoaded(menuItem, result, item, provider, contextMenu, trigger);
            menuItem.SubmenuOpened += (s, e) => { if (e.OriginalSource == menuItem) QuickNavigationSubMenuLoader.EnsureLoaded(menuItem, result, item, provider, contextMenu, trigger); };
        }
        else
        {
            itemPath = QuickNavigationPathResolver.TryResolveCommandPath(provider, item.CommandId);
        }

        if (item.HBitmapItem == IntPtr.Zero && !string.IsNullOrEmpty(itemPath) && item.OnExecute == null)
        {
            if (Helpers.FavoriteUrlHelper.IsWebUrl(itemPath))
            {
                menuItem.Icon = new Image { Source = Helpers.FavoriteUrlHelper.Icon };
            }
            else
            {
                var isDir = item.HasSubMenu;
                var cached = ShellIconHelper.GetIconFromCacheOnly(itemPath, isDir, out var needsLoad);
                if (cached != null) menuItem.Icon = new Image { Source = cached };
                if (needsLoad)
                {
                    Task.Run(() =>
                    {
                        var icon = ShellIconHelper.GetIconForPath(itemPath, isDir);
                        if (icon != null) Application.Current.Dispatcher.BeginInvoke(() => menuItem.Icon = new Image { Source = icon });
                    });
                }
            }
        }

        var canNavigate = !string.IsNullOrEmpty(itemPath) &&
                          (itemPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) ||
                           itemPath.StartsWith("::", StringComparison.OrdinalIgnoreCase) ||
                           Directory.Exists(itemPath) ||
                           File.Exists(itemPath));

        Action triggerAction = () =>
        {
            // Root-level category entries (Favorites/History/configured folders) and any provider-marked
            // non-actionable node (e.g. an ini-defined submenu group with no real target of its own) are
            // pure navigation categories -- clicking/Enter must do nothing at all, not even close the menu.
            // Their contents are still reachable via submenu expansion (hover/keyboard-focus/right-arrow),
            // which is wired independently of this action below. Gated on HasSubMenu, not "isRootItem"
            // alone: a provider can legitimately put a genuinely actionable LEAF at the root too (e.g.
            // CustomCommandsQuickNavProvider's own commands with no configured submenu path), and those
            // must still fire on click/Enter same as any nested leaf does.
            if ((isRootItem && item.HasSubMenu) || !item.IsActionable) return;

            contextMenu.IsOpen = false;
            (contextMenu.PlacementTarget as Window)?.Hide();
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (item.HasSubMenu)
                {
                    if (canNavigate) QuickNavigationNavigator.NavigateOrOpen(itemPath!, isDir: true, trigger);
                }
                else
                {
                    // Plugin-owned action: call OnExecute directly if set.
                    if (item.OnExecute != null)
                        item.OnExecute();
                    else if (!string.IsNullOrEmpty(itemPath))
                        QuickNavigationNavigator.NavigateOrOpen(itemPath, isDir: false, trigger);
                    else
                        provider.ExecuteCommand(result, item.CommandId, IntPtr.Zero);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        };

        // Always intercept the right-click, even when the flyout itself is disabled (root items): WPF's
        // own MenuItem raises Click for a right mouse-button release too, not just left, so an
        // unhandled right-click here was falling through to the same triggerAction() a left-click uses
        // -- right-clicking a root-level leaf command silently ran it instead of doing nothing. Swallowing
        // it unconditionally (e.Handled = true) and only actually showing the flyout when enableRightClick
        // is set keeps root items truly inert on right-click, same as they already are on left-click.
        {
            Action triggerRightClickAction = () => PluginContextMenuHelper.Show(canNavigate, itemPath, item.HasSubMenu, menuItem, contextMenu);

            menuItem.AddHandler(UIElement.PreviewMouseRightButtonUpEvent, new MouseButtonEventHandler((s, e) =>
            {
                if (QuickNavigationMenu.FindVisualParent<MenuItem>(e.OriginalSource as DependencyObject) == menuItem)
                {
                    e.Handled = true;
                    if (enableRightClick) triggerRightClickAction();
                }
            }), handledEventsToo: true);
        }

        if (item.HasSubMenu && item.SubMenuHandle != IntPtr.Zero)
        {
            menuItem.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((s, e) =>
            {
                if (QuickNavigationMenu.FindVisualParent<MenuItem>(e.OriginalSource as DependencyObject) == menuItem)
                {
                    e.Handled = true;
                    triggerAction();
                }
            }), handledEventsToo: true);
        }
        else
        {
            menuItem.Click += (s, e) => { if (e.Source == menuItem) triggerAction(); };
        }

        menuItem.PreviewKeyDown += (s, e) =>
            QuickNavigationMenuKeyHandler.HandlePreviewKeyDown(e, menuItem, item, contextMenu, itemPath, canNavigate, enableRightClick, triggerAction);

        return menuItem;
    }
}
