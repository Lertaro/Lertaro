using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public class LocalSendAliasGeneratorTests
{
    [TestMethod]
    public void GenerateRandomAlias_ChineseCulture_ReturnsDeCombination()
    {
        var alias = LocalSendAliasGenerator.GenerateRandomAlias("zh-CN");

        Assert.IsFalse(string.IsNullOrWhiteSpace(alias));
        StringAssert.Contains(alias, "的");
    }

    [TestMethod]
    public void GenerateRandomAlias_TraditionalChinese_ReturnsEnglishFallback()
    {
        var aliasHk = LocalSendAliasGenerator.GenerateRandomAlias("zh-HK");
        var aliasTw = LocalSendAliasGenerator.GenerateRandomAlias("zh-TW");

        Assert.IsFalse(string.IsNullOrWhiteSpace(aliasHk));
        Assert.IsFalse(string.IsNullOrWhiteSpace(aliasTw));
        // Traditional Chinese should not use "的" (simplified Chinese adjectives formula)
        Assert.DoesNotContain("的", aliasHk);
        Assert.DoesNotContain("的", aliasTw);
        // Fallback to English space-separated alias format
        StringAssert.Contains(aliasHk, " ");
        StringAssert.Contains(aliasTw, " ");
    }

    [TestMethod]
    public void GenerateRandomAlias_EnglishCulture_ReturnsSpaceSeparatedWords()
    {
        var alias = LocalSendAliasGenerator.GenerateRandomAlias("en-US");

        Assert.IsFalse(string.IsNullOrWhiteSpace(alias));
        StringAssert.Contains(alias, " ");
    }

    [TestMethod]
    public void GenerateRandomAlias_JapaneseCulture_ReturnsValidAlias()
    {
        var alias = LocalSendAliasGenerator.GenerateRandomAlias("ja-JP");

        Assert.IsFalse(string.IsNullOrWhiteSpace(alias));
    }

    [TestMethod]
    public void GenerateRandomAlias_MultipleCalls_GeneratesVariedNames()
    {
        var results = new HashSet<string>();
        for (var i = 0; i < 20; i++)
        {
            results.Add(LocalSendAliasGenerator.GenerateRandomAlias("zh-CN"));
        }

        Assert.IsGreaterThan(1, results.Count);
    }

    [TestMethod]
    public void GetLocalDeviceHashtag_ReturnsValidHashtag()
    {
        var hashtag = LocalSendServerHelper.GetLocalDeviceHashtag();

        Assert.IsFalse(string.IsNullOrWhiteSpace(hashtag));
        StringAssert.StartsWith(hashtag, "#");
    }

    [TestMethod]
    public async Task RequestUserAcceptanceAsync_UserAccepts_ReturnsTrue()
    {
        using var server = new LocalSendServer();
        server.UploadRequested += (s, e) => e.Respond(true);

        var dto = new PrepareUploadRequestDto
        {
            Info = new LocalSendDeviceInfo(),
            Files = new Dictionary<string, LocalSendFileDto>
            {
                ["f1"] = new LocalSendFileDto { Id = "f1", FileName = "test.png", Size = 100 }
            }
        };

        var (accepted, _, _) = await server.RequestUserAcceptanceAsync("test-session-id", dto);
        Assert.IsTrue(accepted);
    }

    [TestMethod]
    public void CheckPin_ValidPin_ReturnsTrue()
    {
        using var server = new LocalSendServer();
        server.ReceivePin = "1234";

        var ok = server.CheckPin("192.168.1.50", "1234", out var status, out var errBody);

        Assert.IsTrue(ok);
        Assert.AreEqual(200, status);
        Assert.IsNull(errBody);
    }

    [TestMethod]
    public void CheckPin_InvalidPin_Returns401()
    {
        using var server = new LocalSendServer();
        server.ReceivePin = "1234";

        var ok = server.CheckPin("192.168.1.50", "9999", out var status, out var errBody);

        Assert.IsFalse(ok);
        Assert.AreEqual(401, status);
        Assert.IsNotNull(errBody);
        StringAssert.Contains(errBody, "Invalid PIN");
    }

    [TestMethod]
    public void CheckPin_MultipleFailures_Triggers429()
    {
        using var server = new LocalSendServer();
        server.ReceivePin = "1234";

        server.CheckPin("192.168.1.50", "0001", out _, out _);
        server.CheckPin("192.168.1.50", "0002", out _, out _);
        server.CheckPin("192.168.1.50", "0003", out _, out _);
        var ok = server.CheckPin("192.168.1.50", "0004", out var status, out var errBody);

        Assert.IsFalse(ok);
        Assert.AreEqual(429, status);
        Assert.IsNotNull(errBody);
        StringAssert.Contains(errBody, "Too many requests");
    }
}
