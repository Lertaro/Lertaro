using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Services.Plugin;

public class PluginSearchResult : ISearchResult
{
    public PluginSearchResult(string name, string argumentText, string contextDirectory)
    {
        Name = name;
        FullPath = argumentText;
        ContextDirectory = contextDirectory;
    }

    public string Name { get; }
    public string FullPath { get; }
    public string ContextDirectory { get; }
    public bool IsDir => false;
    public bool IsApplication => false;
}
