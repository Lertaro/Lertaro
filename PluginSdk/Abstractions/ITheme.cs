using System.Windows;

namespace Lertaro.PluginSdk.Abstractions;

public interface ITheme
{
    string Id { get; }
    string DisplayName { get; }
    bool IsDark { get; }
    ResourceDictionary GetResources();

    double WindowOpacity => 1.0;
}
