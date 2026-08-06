using Lertaro.App.Services.PluginManagerCore;

namespace Lertaro.App.Tests.Services.PluginManagerCore;

[TestClass]
public sealed class ComponentFilterTests
{
    [TestMethod]
    public void GetDllName_ReturnsAssemblyFileNameOfObjectType() =>
        Assert.AreEqual("Lertaro.App.Tests.dll", ComponentFilter.GetDllName(new ComponentFilterTests()));

    [TestMethod]
    public void GetDllName_DifferentAssembly_ReturnsThatAssemblysFileName() =>
        // A type from App itself (ComponentFilter's own assembly), not this test assembly.
        Assert.AreEqual("Lertaro.App.dll", ComponentFilter.GetDllName(new ComponentFilter()));

    [TestMethod]
    public void GetDllName_FrameworkType_ReturnsItsAssemblyFileName() =>
        Assert.AreEqual("System.Private.CoreLib.dll", ComponentFilter.GetDllName(42));
}
