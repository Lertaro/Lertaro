using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.Tests;

[TestClass]
public static class AppTestAssembly
{
    // Result-list tests must not depend on what this machine has opened recently.
    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        WindowsRecentCandidateMapper.Source = () => [];

        // The calibration log is user data: a synthesized query ("atlas", "test") cannot be told
        // apart from a real one once it is hashed, so tests must never write to it.
        RankingEventLog.EnabledOverride = false;
    }
}
