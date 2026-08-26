using Lertaro.PluginSdk.Models;
using Lertaro.Plugins.FolderCascader.Navigation;

namespace Lertaro.Plugins.FolderCascader.Tests.Navigation;

[TestClass]
public sealed class MenuBuilderContentExtensionsTests
{
    [TestMethod]
    public void HasAvailableFavorites_IgnoresUnavailablePaths()
    {
        var favorites = new[]
        {
            new FavoriteItem { Path = @"C:\Missing" },
            new FavoriteItem { Path = "" }
        };

        var available = MenuBuilderContentExtensions.HasAvailableFavorites(favorites, _ => false, _ => false);

        Assert.IsFalse(available);
    }

    [TestMethod]
    public void HasAvailableFavorites_AcceptsVirtualFolderWithoutProbingTheFileSystem()
    {
        var favorites = new[] { new FavoriteItem { Path = "shell:AppsFolder" } };

        var available = MenuBuilderContentExtensions.HasAvailableFavorites(
            favorites,
            _ => throw new AssertFailedException("A virtual folder must not be probed."),
            _ => throw new AssertFailedException("A virtual folder must not be probed."));

        Assert.IsTrue(available);
    }

    [TestMethod]
    public void HasAvailableFavorites_AcceptsExistingFolder()
    {
        var favorites = new[] { new FavoriteItem { Path = @"C:\Existing" } };

        var available = MenuBuilderContentExtensions.HasAvailableFavorites(favorites, _ => false, path => path == @"C:\Existing");

        Assert.IsTrue(available);
    }

    [TestMethod]
    public void HasAvailableFavorites_AcceptsFolderWithEnvironmentVariables()
    {
        var favorites = new[] { new FavoriteItem { Path = @"%TEST_ENV_VAR%\SubFolder" } };
        Environment.SetEnvironmentVariable("TEST_ENV_VAR", @"C:\TestDir");
        try
        {
            var available = MenuBuilderContentExtensions.HasAvailableFavorites(
                favorites,
                _ => false,
                path => path == @"C:\TestDir\SubFolder");

            Assert.IsTrue(available);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_ENV_VAR", null);
        }
    }
}
