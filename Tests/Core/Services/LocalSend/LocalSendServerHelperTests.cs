using System.Net;
using System.Text;
using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public class LocalSendServerHelperTests
{
    [TestMethod]
    public void FormatIpAddress_IPv4MappedIPv6_UnmapsToStandardIPv4()
    {
        var mappedIp = IPAddress.Parse("::ffff:192.168.1.100");

        var result = LocalSendServerHelper.FormatIpAddress(mappedIp);

        Assert.AreEqual("192.168.1.100", result);
    }

    [TestMethod]
    public void FormatIpAddress_StandardIPv4_ReturnsSameString()
    {
        var ipv4 = IPAddress.Parse("192.168.1.50");

        var result = LocalSendServerHelper.FormatIpAddress(ipv4);

        Assert.AreEqual("192.168.1.50", result);
    }

    [TestMethod]
    public void FormatIpAddress_StandardIPv6_ReturnsStandardString()
    {
        var ipv6 = IPAddress.Parse("fe80::1");

        var result = LocalSendServerHelper.FormatIpAddress(ipv6);

        Assert.AreEqual("fe80::1", result);
    }

    [TestMethod]
    public async Task WriteResponseAsync_StatusOK_WritesValidHttpResponseHeaders()
    {
        using var ms = new MemoryStream();

        await LocalSendServerHelper.WriteResponseAsync(ms, 200).ConfigureAwait(false);

        var output = Encoding.UTF8.GetString(ms.ToArray());
        StringAssert.Contains(output, "HTTP/1.1 200 OK\r\n");
        StringAssert.Contains(output, "Content-Type: application/json; charset=utf-8\r\n");
        StringAssert.Contains(output, "Transfer-Encoding: chunked\r\n");
        StringAssert.EndsWith(output, "\r\n0\r\n\r\n");
    }

    [TestMethod]
    public async Task WriteResponseAsync_WithJsonBody_WritesChunkedJson()
    {
        using var ms = new MemoryStream();
        var json = "{\"alias\":\"test\"}";

        await LocalSendServerHelper.WriteResponseAsync(ms, 200, json).ConfigureAwait(false);

        var output = Encoding.UTF8.GetString(ms.ToArray());
        StringAssert.Contains(output, "HTTP/1.1 200 OK\r\n");
        StringAssert.Contains(output, "Content-Type: application/json; charset=utf-8\r\n");
        StringAssert.Contains(output, "Transfer-Encoding: chunked\r\n");
        StringAssert.Contains(output, json);
    }

    [TestMethod]
    public async Task WriteResponseAsync_UsesStandardUnauthorizedAndRateLimitPhrases()
    {
        await using var unauthorized = new MemoryStream();
        await LocalSendServerHelper.WriteResponseAsync(unauthorized, 401);
        await using var rateLimited = new MemoryStream();
        await LocalSendServerHelper.WriteResponseAsync(rateLimited, 429);

        StringAssert.Contains(Encoding.UTF8.GetString(unauthorized.ToArray()), "401 Unauthorized");
        StringAssert.Contains(Encoding.UTF8.GetString(rateLimited.ToArray()), "429 Too Many Requests");
    }

    [TestMethod]
    public void ResolveTargetPath_RelativeFolderStructure_CreatesSubDirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LocalSendTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var rawFileName = "folderA/subB/test.txt";
            var targetPath = LocalSendServerHelper.ResolveTargetPath(tempDir, rawFileName);

            Assert.IsNotNull(targetPath);
            StringAssert.EndsWith(targetPath, Path.Combine("folderA", "subB", "test.txt"));
            Assert.IsTrue(Directory.Exists(Path.Combine(tempDir, "folderA", "subB")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [TestMethod]
    public void ResolveTargetPath_PathWithMatchingPrefixOutsideTheDestination_IsRejected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LocalSendTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var targetPath = LocalSendServerHelper.ResolveTargetPath(tempDir, "../" + Path.GetFileName(tempDir) + "_other/test.txt");

            Assert.IsNull(targetPath);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void ResolveTargetPath_WindowsIllegalFileNameCharacters_AreLegalized()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LocalSendTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var targetPath = LocalSendServerHelper.ResolveTargetPath(tempDir, "report?.txt");

            Assert.IsNotNull(targetPath);
            Assert.AreEqual("report_.txt", Path.GetFileName(targetPath));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void ResolveTargetPath_IllegalDirectoryComponents_AreLegalized()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LocalSendTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var targetPath = LocalSendServerHelper.ResolveTargetPath(tempDir, "bad?/CON/report.txt");

            Assert.IsNotNull(targetPath);
            StringAssert.EndsWith(targetPath, Path.Combine("bad_", "_CON", "report.txt"));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void BuildCancellationUri_UsesOnlyTheAdvertisedProtocolVersion()
    {
        var v1 = LocalSendServerHelper.BuildCancellationUri(new LocalSendDeviceInfo { IpAddress = "192.168.1.20", Port = 53317, Version = "1.0" }, "session");
        var v2 = LocalSendServerHelper.BuildCancellationUri(new LocalSendDeviceInfo { IpAddress = "192.168.1.20", Port = 53317, Version = "2.1" }, "session");

        Assert.AreEqual("http://192.168.1.20:53317/api/localsend/v1/cancel", v1);
        Assert.AreEqual("http://192.168.1.20:53317/api/localsend/v2/cancel?sessionId=session", v2);
    }

    [TestMethod]
    public void FormatDeviceHashtag_UsesNetworkInterfaceAddressOrderAndDistinctSuffixes()
    {
        var hashtag = LocalSendServerHelper.FormatDeviceHashtag([
            IPAddress.Parse("10.0.0.84"),
            IPAddress.Parse("169.254.184.80"),
            IPAddress.Parse("10.0.0.1"),
            IPAddress.Parse("10.230.212.111"),
            IPAddress.Parse("192.168.233.1"),
            IPAddress.Parse("192.168.225.1")
        ]);

        Assert.AreEqual("#84 / #111 / #1", hashtag);
    }
}
