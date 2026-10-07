using Lertaro.App.Services.Theme;

namespace Lertaro.App.Tests.Services.Theme;

[TestClass]
public sealed class BuiltInThemeTests
{
    [STATestMethod]
    [DataRow("Light", false)]
    [DataRow("Dark", true)]
    public void GetResources_UsesHostAssemblyWithoutLoadingCoreExtensions(string id, bool isDark)
    {
        _ = System.Windows.Application.ResourceAssembly;
        var theme = new BuiltInTheme(id, isDark);
        Assert.IsTrue(theme.GetResources().Contains("TextPrimary"));
        Assert.AreEqual(id, theme.GetResources()["ThemeId"]);
        Assert.AreEqual(isDark, theme.GetResources()["IsDark"]);
        Assert.Contains("/Lertaro.App;component/", theme.GetResources().Source.AbsoluteUri);
    }
}
