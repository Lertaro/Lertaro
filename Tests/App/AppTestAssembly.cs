using Lertaro.App.ViewModels.Search.Mapping;

namespace Lertaro.App.Tests;

[TestClass]
public static class AppTestAssembly
{
    // Result-list tests must not depend on what this machine has opened recently.
    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        WindowsRecentCandidateMapper.Source = () => [];
    }
}
