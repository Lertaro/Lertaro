using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.SpanishAlias;

public sealed class SpanishAliasPlugin : IPlugin
{
    public string Id => "Lertaro.Plugins.SpanishAlias";
    public string Name => "Spanish Alias";
    public string Version => "1.0.0";
    public string Author => "Lertaro";
}
