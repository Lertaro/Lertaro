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
        Assert.AreEqual("{\"message\":\"Invalid PIN\"}", body);
    }

    [TestMethod]
    public void CheckPin_MissingPin_ReturnsProtocolMessageWithoutIncrementingAttempts()
    {
        var attempts = new ConcurrentDictionary<string, int>();

        var valid = LocalSendPinValidator.CheckPin("1234", attempts, "127.0.0.1", null, out var status, out var body);

        Assert.IsFalse(valid);
        Assert.AreEqual(401, status);
        Assert.AreEqual("{\"message\":\"PIN required\"}", body);
        Assert.IsFalse(attempts.ContainsKey("127.0.0.1"));
    }

    [TestMethod]
    public void CheckPin_ThirdWrongAttemptIsUnauthorizedAndFourthIsRateLimited()
    {
        var attempts = new ConcurrentDictionary<string, int>();

        for (var i = 0; i < 3; i++)
        {
            var valid = LocalSendPinValidator.CheckPin("1234", attempts, "127.0.0.1", "9999", out var status, out _);
            Assert.IsFalse(valid);
            Assert.AreEqual(401, status);
        }

        var blocked = LocalSendPinValidator.CheckPin("1234", attempts, "127.0.0.1", "1234", out var blockedStatus, out var body);
        Assert.IsFalse(blocked);
        Assert.AreEqual(429, blockedStatus);
        Assert.AreEqual("{\"message\":\"Too many requests\"}", body);
    }

    [TestMethod]
    public void CheckPin_CorrectPinClearsPreviousFailures()
    {
        var attempts = new ConcurrentDictionary<string, int> { ["127.0.0.1"] = 2 };

        var valid = LocalSendPinValidator.CheckPin("1234", attempts, "127.0.0.1", "1234", out _, out _);

        Assert.IsTrue(valid);
        Assert.IsFalse(attempts.ContainsKey("127.0.0.1"));
    }

    [TestMethod]
    public void CheckPin_LockoutExpiresAfterDuration_AllowsRetry()
    {
        var attempts = new ConcurrentDictionary<string, int>();
        var attemptTimes = new ConcurrentDictionary<string, DateTime>();

        for (var i = 0; i < 3; i++)
            LocalSendPinValidator.CheckPin("1234", attempts, "127.0.0.1", "9999", out _, out _, attemptTimes);

        var blocked = LocalSendPinValidator.CheckPin("1234", attempts, "127.0.0.1", "1234", out var blockedStatus, out _, attemptTimes);
        Assert.IsFalse(blocked);
        Assert.AreEqual(429, blockedStatus);

        attemptTimes["127.0.0.1"] = DateTime.UtcNow.AddMinutes(-6);
        var allowed = LocalSendPinValidator.CheckPin("1234", attempts, "127.0.0.1", "1234", out var allowedStatus, out _, attemptTimes);

        Assert.IsTrue(allowed);
        Assert.AreEqual(200, allowedStatus);
        Assert.IsFalse(attempts.ContainsKey("127.0.0.1"));
    }

}
