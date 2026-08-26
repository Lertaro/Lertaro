using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

using Lertaro.App.Services;
using Lertaro.App.Services.ShellIcons;

using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfBinding = System.Windows.Data.Binding;
using WpfImage = System.Windows.Controls.Image;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfSeparator = System.Windows.Controls.Separator;

namespace Lertaro.App.Views.Settings;

public partial class FavoritesSettingsPage : System.Windows.Controls.UserControl
{
    private sealed record PresetMenuItemModel(
        string? TranslationKey,
        string? CommandParameter,
        string? IconPathKey,
        IReadOnlyList<PresetMenuItemModel>? Children = null,
        bool IsSeparator = false);

    private static readonly IReadOnlyList<PresetMenuItemModel> PresetMenu =
    [
        new("Favorites_PresetUser", "%USERPROFILE%", "UserProfile"),
        new("Favorites_PresetAppData", "%USERPROFILE%\\AppData", "AppData"),
        new("Favorites_PresetRoaming", "%APPDATA%", "Roaming"),
        new("Favorites_PresetLocalTemp", "%TEMP%", "LocalTemp"),
        new(null, null, null, IsSeparator: true),
        new(
            "Favorites_PresetUserFolders",
            null,
            "UserFolders",
            [
                new("Favorites_PresetDesktop", "shell:Desktop", "Desktop"),
                new("Favorites_PresetDownloads", "shell:Downloads", "Downloads"),
                new("Favorites_PresetDocuments", "shell:Personal", "Documents"),
                new("Favorites_PresetMusic", "shell:My Music", "Music"),
                new("Favorites_PresetPictures", "shell:My Pictures", "Pictures"),
                new("Favorites_PresetVideos", "shell:My Video", "Videos")
            ])
    ];

    public FavoritesSettingsPage() => InitializeComponent();

    private void OpenPresetMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.ContextMenu == null) return;

        // A ContextMenu attached to a Button only opens on right-click by default; the chevron
        // button must open it on a normal left-click too.
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        EnsurePresetMenu(button.ContextMenu);
        ApplyPresetMenuIcons(button.ContextMenu);
        button.ContextMenu.IsOpen = true;
    }

    private static void EnsurePresetMenu(WpfContextMenu menu)
    {
        if (menu.Items.Count != 0)
        {
            return;
        }

        foreach (var definition in PresetMenu)
        {
            menu.Items.Add(CreatePresetMenuEntry(definition));
        }
    }

    private static object CreatePresetMenuEntry(PresetMenuItemModel definition)
    {
        if (definition.IsSeparator)
        {
            return new WpfSeparator();
        }

        var item = new WpfMenuItem { Tag = definition.IconPathKey };
        item.SetBinding(
            System.Windows.Controls.HeaderedItemsControl.HeaderProperty,
            new WpfBinding($"[{definition.TranslationKey}]") { Source = TranslationManager.Instance });

        if (definition.CommandParameter != null)
        {
            item.CommandParameter = definition.CommandParameter;
            item.SetBinding(
                WpfMenuItem.CommandProperty,
                new WpfBinding("PlacementTarget.DataContext.AddPathPresetCommand")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(WpfContextMenu), 1)
                });
        }

        if (definition.Children != null)
        {
            foreach (var child in definition.Children)
            {
                item.Items.Add(CreatePresetMenuEntry(child));
            }
        }

        return item;
    }

    private static void ApplyPresetMenuIcons(WpfContextMenu menu)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Path.Combine(userProfile, "AppData");
        var iconPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["UserProfile"] = userProfile,
            ["AppData"] = appData,
            ["Roaming"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ["LocalTemp"] = Path.GetTempPath(),
            ["UserFolders"] = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}",
            ["Desktop"] = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ["Downloads"] = Path.Combine(userProfile, "Downloads"),
            ["Documents"] = Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            ["Music"] = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            ["Pictures"] = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            ["Videos"] = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        foreach (var entry in menu.Items)
        {
            if (entry is WpfMenuItem item)
            {
                ApplyPresetMenuItemIcon(item, iconPaths);
            }
        }
    }

    private static void ApplyPresetMenuItemIcon(WpfMenuItem item, IReadOnlyDictionary<string, string> iconPaths)
    {
        if (item.Tag is string tag)
        {
            if (iconPaths.TryGetValue(tag, out var path))
            {
                var icon = path.GetIconForPath(isDir: true);
                if (icon != null)
                {
                    item.Icon = new WpfImage
                    {
                        Source = icon,
                        Width = 16,
                        Height = 16,
                        Stretch = Stretch.Uniform
                    };
                }
            }
        }

        foreach (var child in item.Items)
        {
            if (child is WpfMenuItem childItem)
            {
                ApplyPresetMenuItemIcon(childItem, iconPaths);
            }
        }
    }
}
