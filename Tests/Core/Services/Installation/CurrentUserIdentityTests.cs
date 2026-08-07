using Lertaro.Core.Services.Installation;

namespace Lertaro.Core.Tests.Services.Installation;

[TestClass]
public sealed class CurrentUserIdentityTests
{
    [TestMethod]
    public void Hash_UsesTheCacheKeyHashFormat() => Assert.AreEqual(
        "20d80484069962670c7a67191a3734f41b2f1759e466d2e061e1d8220a3b0ee2",
        CurrentUserIdentity.Hash("S-1-5-21-100"));
}
