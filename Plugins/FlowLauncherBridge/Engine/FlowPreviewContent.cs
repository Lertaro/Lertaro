using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Flow.Launcher.Plugin;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>Builds a selectable preview from the SDK's image/description metadata.</summary>
internal static class FlowPreviewContent
{
    internal static bool HasContent(Result result) => result.Preview is { } preview &&
        (!string.IsNullOrEmpty(preview.Description) || !string.IsNullOrEmpty(preview.PreviewImagePath)
         || preview.PreviewDelegate != null || !string.IsNullOrEmpty(preview.FilePath));

    internal static UserControl Create(Result result)
    {
        var preview = result.Preview ?? new();
        var panel = new StackPanel { Margin = new Thickness(16) };
        var image = preview.PreviewDelegate?.Invoke();
        var path = preview.PreviewImagePath;
        if (image == null && !string.IsNullOrEmpty(path))
        {
            if (!Path.IsPathRooted(path)) path = Path.Combine(result.PluginDirectory, path);
            image = FlowIconLoader.LoadIconAsBitmapSource(path);
        }
        if (image != null && preview.ContentType != PreviewContentType.Markdown)
            panel.Children.Add(new Image { Source = image, MaxHeight = 400, Stretch = Stretch.Uniform });
        panel.Children.Add(new TextBlock { Text = result.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        // ponytail: render Markdown as selectable source until the host has a shared Markdown renderer.
        panel.Children.Add(new TextBox
        {
            Text = preview.Description ?? result.SubTitle,
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Margin = new Thickness(0, 12, 0, 0)
        });
        return new UserControl { Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
    }
}
