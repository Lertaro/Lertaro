using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.CoreExtensions.Actions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Actions;

[TestClass]
[DoNotParallelize]
public sealed class AddFavoriteActionTests
{
    [TestInitialize]
    public void ResetFavoriteService()
    {
        FavoritesService.GetFavoritesFunc = null;
        FavoritesService.IsFavoriteFunc = null;
        FavoritesService.AddFavoriteFunc = null;
    }

    [TestCleanup]
    public void ClearFavoriteService() => ResetFavoriteService();

    [TestMethod]
    public void CanExecute_ExistingUnfavoritedFile_ReturnsTrue()
    {
        using var file = new TemporaryFile();
        FavoritesService.IsFavoriteFunc = _ => false;

        Assert.IsTrue(new AddFavoriteAction().CanExecute(new[] { CreateResult(file.Path) }));
    }

    [TestMethod]
    public void CanExecute_AlreadyFavoritedFile_ReturnsFalse()
    {
        using var file = new TemporaryFile();
        FavoritesService.IsFavoriteFunc = _ => true;

        Assert.IsFalse(new AddFavoriteAction().CanExecute(new[] { CreateResult(file.Path) }));
    }

    [TestMethod]
    public void CanExecute_MultipleOrMissingResults_ReturnsFalse()
    {
        using var file = new TemporaryFile();
        FavoritesService.IsFavoriteFunc = _ => false;
        var result = CreateResult(file.Path);

        Assert.IsFalse(new AddFavoriteAction().CanExecute(new[] { result, result }));
        Assert.IsFalse(new AddFavoriteAction().CanExecute(Array.Empty<ISearchResult>()));
    }

    private static ISearchResult CreateResult(string path) => new FakeResult
    {
        Name = Path.GetFileName(path),
        FullPath = path
    };

    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = string.Empty;
        public string FullPath { get; init; } = string.Empty;
        public string ContextDirectory { get; init; } = string.Empty;
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    private sealed class TemporaryFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lertaro-favorite-{Guid.NewGuid():N}.tmp");

        public TemporaryFile() => File.WriteAllText(Path, string.Empty);

        public void Dispose()
        {
            try { File.Delete(Path); } catch { }
        }
    }
}
