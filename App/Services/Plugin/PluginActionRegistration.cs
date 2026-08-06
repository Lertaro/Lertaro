using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Services.Plugin;

public sealed record PluginActionRegistration(uint RuntimeActionId, IPlugin Plugin, ISearchResultAction Action);
