using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.SettingsTemplate;

/// <summary>
/// Dynamically constructs WPF settings panel from Flow.Launcher SettingsTemplate.yaml/json and binds to Settings.json.
/// </summary>
public static class FlowSettingsTemplateBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Control BuildSettingsPanel(string templateFilePath, string settingsJsonPath)
    {
        var doc = FlowSettingsTemplateParser.ParseFile(templateFilePath);
        var settings = FlowSettingsTemplateStorage.LoadSettings(settingsJsonPath);

        var rootPanel = new StackPanel
        {
            Margin = new Thickness(16, 12, 16, 16)
        };

        foreach (var elem in doc.Elements)
        {
            var control = CreateControlForElement(elem, settings, settingsJsonPath);
            if (control != null)
            {
                rootPanel.Children.Add(control);
            }
        }

        return new ScrollViewer
        {
            Content = rootPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private static FrameworkElement? CreateControlForElement(
        FlowSettingsTemplateElement elem,
        JsonObject settings,
        string settingsJsonPath)
    {
        var type = elem.Type.ToLowerInvariant();
        return type switch
        {
            "textblock" => BuildTextBlock(elem),
            "checkbox" => BuildCheckBox(elem, settings, settingsJsonPath),
            "input" or "txtbox" or "textbox" => BuildTextBox(elem, settings, settingsJsonPath),
            "select" or "dropdown" => BuildDropdown(elem, settings, settingsJsonPath),
            "hyperlink" => BuildHyperlink(elem),
            _ => null
        };
    }

    private static FrameworkElement BuildTextBlock(FlowSettingsTemplateElement elem)
    {
        var text = !string.IsNullOrEmpty(elem.Description) ? elem.Description : elem.Label;
        if (string.IsNullOrEmpty(text)) text = elem.Name;

        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Margin = new Thickness(0, 4, 0, 10),
            Opacity = 0.9
        };
    }

    private static FrameworkElement BuildCheckBox(
        FlowSettingsTemplateElement elem,
        JsonObject settings,
        string settingsJsonPath)
    {
        var container = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
        var key = elem.Name;

        var isChecked = false;
        if (settings.TryGetPropertyValue(key, out var node) && node != null)
        {
            if (node.GetValueKind() == JsonValueKind.True) isChecked = true;
            else if (node.GetValueKind() == JsonValueKind.False) isChecked = false;
            else if (bool.TryParse(node.ToString(), out var b)) isChecked = b;
        }
        else if (bool.TryParse(elem.DefaultValue, out var defBool))
        {
            isChecked = defBool;
        }

        var cb = new CheckBox
        {
            Content = !string.IsNullOrEmpty(elem.Label) ? elem.Label : key,
            IsChecked = isChecked,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        cb.Checked += (_, _) =>
        {
            settings[key] = true;
            FlowSettingsTemplateStorage.SaveSettings(settingsJsonPath, settings);
        };
        cb.Unchecked += (_, _) =>
        {
            settings[key] = false;
            FlowSettingsTemplateStorage.SaveSettings(settingsJsonPath, settings);
        };

        container.Children.Add(cb);

        if (!string.IsNullOrEmpty(elem.Description))
        {
            var desc = new TextBlock
            {
                Text = elem.Description,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Opacity = 0.6,
                Margin = new Thickness(24, 2, 0, 0)
            };
            container.Children.Add(desc);
        }

        return container;
    }

    private static FrameworkElement BuildTextBox(
        FlowSettingsTemplateElement elem,
        JsonObject settings,
        string settingsJsonPath)
    {
        var container = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
        var key = elem.Name;

        if (!string.IsNullOrEmpty(elem.Label))
        {
            container.Children.Add(new TextBlock
            {
                Text = elem.Label,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });
        }

        var currentVal = string.Empty;
        if (settings.TryGetPropertyValue(key, out var node) && node != null)
        {
            currentVal = node.ToString();
        }
        else
        {
            currentVal = elem.DefaultValue;
        }

        var tb = new TextBox
        {
            Text = currentVal,
            FontSize = 13,
            Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        tb.TextChanged += (_, _) =>
        {
            settings[key] = tb.Text;
            FlowSettingsTemplateStorage.SaveSettings(settingsJsonPath, settings);
        };

        container.Children.Add(tb);

        if (!string.IsNullOrEmpty(elem.Description))
        {
            container.Children.Add(new TextBlock
            {
                Text = elem.Description,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Opacity = 0.6,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        return container;
    }

    private static FrameworkElement BuildDropdown(
        FlowSettingsTemplateElement elem,
        JsonObject settings,
        string settingsJsonPath)
    {
        var container = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
        var key = elem.Name;

        if (!string.IsNullOrEmpty(elem.Label))
        {
            container.Children.Add(new TextBlock
            {
                Text = elem.Label,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });
        }

        var cb = new ComboBox
        {
            Height = 30,
            FontSize = 13,
            ItemsSource = elem.Options
        };

        var selectedVal = string.Empty;
        if (settings.TryGetPropertyValue(key, out var node) && node != null)
            selectedVal = node.ToString();
        else
            selectedVal = elem.DefaultValue;

        if (!string.IsNullOrEmpty(selectedVal))
            cb.SelectedItem = selectedVal;
        else if (elem.Options.Count > 0)
            cb.SelectedIndex = 0;

        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem != null)
            {
                settings[key] = cb.SelectedItem.ToString();
                FlowSettingsTemplateStorage.SaveSettings(settingsJsonPath, settings);
            }
        };

        container.Children.Add(cb);
        return container;
    }

    private static FrameworkElement BuildHyperlink(FlowSettingsTemplateElement elem)
    {
        var url = !string.IsNullOrEmpty(elem.Url) ? elem.Url : elem.Name;
        var label = !string.IsNullOrEmpty(elem.Label) ? elem.Label : url;

        var tb = new TextBlock { Margin = new Thickness(0, 4, 0, 8) };
        var link = new Hyperlink(new Run(label))
        {
            NavigateUri = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null
        };
        link.RequestNavigate += (_, e) =>
        {
            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
        };
        tb.Inlines.Add(link);
        return tb;
    }
}
