using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.FolderCascader.Navigation;

namespace Lertaro.Plugins.FolderCascader.Tests;

// Shared MenuBuilder test setup. Classes must use DoNotParallelize because they replace the shared
// PluginSettingsService and PluginPromptService delegates.
internal static class MenuBuilderTestHelpers
{
    internal sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    internal static FolderCascaderPlugin.FolderConfigItem Folder(string name, string path, string subMenu = "") =>
        new() { Name = name, Path = path, SubMenu = subMenu };

    internal static string GetPath(Provider provider, IntPtr handle)
    {
        Assert.IsTrue(provider.TryGetPath(handle, out var path));
        return path!;
    }

    internal sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
