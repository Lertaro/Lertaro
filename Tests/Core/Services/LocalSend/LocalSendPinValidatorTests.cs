using System.Collections.Concurrent;
using Lertaro.Core.Services.LocalSend;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendPinValidatorTests
{
    [TestMethod]
    public void CheckPin_NoConfiguredPin_ReturnsTrue()
    {
        var attempts = new ConcurrentDictionary<string, int>();
        var valid = LocalSendPinValidator.CheckPin(null, attempts, "127.0.0.1", null, out var status, out var body);
        Assert.IsTrue(valid);
        Assert.AreEqual(200, status);
        Assert.IsNull(body);
    }

    [TestMethod]
    public void CheckPin_CorrectPin_ReturnsTrue()
    {
        var attempts = new ConcurrentDictionary<string, int>();
        var valid = LocalSendPinValidator.CheckPin("1234", attempts, "127.0.0.1", "1234", out var status, out var body);
        Assert.IsTrue(valid);
        Assert.AreEqual(200, status);
    }

    [TestMethod]
    public void CheckPin_IncorrectPin_ReturnsUnauthorized()
    {
        var attempts = new ConcurrentDictionary<string, int>();
        var valid = LocalSendPinValidator.CheckPin("1234", attempts, "127.0.0.1", "9999", out var status, out var body);
        Assert.IsFalse(valid);
        Assert.AreEqual(401, status);
    }
}
