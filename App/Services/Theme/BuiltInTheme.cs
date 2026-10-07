using System.Windows;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.Services.Theme;

// Host-owned resources keep Settings usable even when CoreExtensions is disabled.
internal sealed class BuiltInTheme(string id, bool isDark) : ITheme
{
    private ResourceDictionary? _resources;
    public string Id => id;
    public string DisplayName => TranslationService.Get($"Theme_{Id}");
    public bool IsDark => isDark;
    public double WindowOpacity => (double)GetResources()["WindowOpacity"];
    public ResourceDictionary GetResources() => _resources ??= new ResourceDictionary
    {
        Source = new Uri($"pack://application:,,,/Lertaro.App;component/Resources/Themes/{Id}.xaml", UriKind.Absolute)
    };
}
