using System.Net;
using System.Text;
using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
public sealed class LocalSendServerHandlerTests
{
    [TestMethod]
    public async Task ProcessAsync_InfoResponse_UsesTheInfoWireDto()
    {
        var server = new LocalSendServer
        {
            DeviceInfo = new LocalSendDeviceInfo { Alias = "Test device", Fingerprint = "test-fingerprint", Port = 54321 }
        };

        var response = await ProcessAsync(server, "GET /api/localsend/v2/info HTTP/1.1\r\nHost: test\r\n\r\n");

        StringAssert.Contains(response, "\"fingerprint\":\"test-fingerprint\"");
        Assert.IsFalse(response.Contains("\"port\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ProcessAsync_V1PrepareUpload_ReturnsOnlyTheFileTokens()
    {
        var body = """
        {"info":{"alias":"Sender","version":"1.0"},"files":{"file-1":{"id":"file-1","fileName":"test.txt","size":0,"fileType":"text"}}}
        """;
        var request = $"POST /api/localsend/v1/send-request HTTP/1.1\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";
        var server = new LocalSendServer { QuickSave = true };

        var response = await ProcessAsync(server, request);

        StringAssert.Contains(response, "\"file-1\"");
        Assert.IsFalse(response.Contains("sessionId", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ProcessAsync_PrepareUploadWithNoSelectedFiles_ReturnsNoContent()
    {
        const string body = "{\"info\":{\"alias\":\"Sender\"},\"files\":{\"file-1\":{\"id\":\"file-1\",\"fileName\":\"test.txt\",\"size\":0,\"fileType\":\"text/plain\"}}}";
        var request = $"POST /api/localsend/v2/prepare-upload HTTP/1.1\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";
        var server = new LocalSendServer();
        server.UploadRequested += (_, args) => { args.SelectedFileIds = []; args.Respond(true); };

        var response = await ProcessAsync(server, request);

        StringAssert.Contains(response, "HTTP/1.1 204 No Content");
    }

    [TestMethod]
    public async Task ProcessAsync_MalformedRegistration_ReturnsBadRequest()
    {
        const string body = "not-json";
        var request = $"POST /api/localsend/v2/register HTTP/1.1\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";

        var response = await ProcessAsync(new LocalSendServer(), request);

        StringAssert.Contains(response, "HTTP/1.1 400 Bad Request");
    }

    [TestMethod]
    public async Task ProcessAsync_UploadWithoutSession_ReturnsNoSessionBeforeParameterValidation()
    {
        const string body = "data";
        var request = $"POST /api/localsend/v2/upload HTTP/1.1\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";

        var response = await ProcessAsync(new LocalSendServer(), request);

        StringAssert.Contains(response, "HTTP/1.1 409 Conflict");
        StringAssert.Contains(response, "\"message\":\"No session\"");
    }

    [TestMethod]
    public async Task ProcessAsync_UploadWithSessionAndMissingParameters_ReturnsBadRequest()
    {
        const string body = "data";
        var request = $"POST /api/localsend/v2/upload HTTP/1.1\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";
        var server = new LocalSendServer();
        server.RegisterActiveSession("session", new PrepareUploadRequestDto { Info = new LocalSendDeviceInfo { IpAddress = "192.168.1.20" } });

        var response = await ProcessAsync(server, request);

        StringAssert.Contains(response, "HTTP/1.1 400 Bad Request");
        StringAssert.Contains(response, "\"message\":\"Missing parameters\"");
    }

    [TestMethod]
    public async Task ProcessAsync_UnknownRoute_ReturnsOfficialPlainTextResponse()
    {
        var response = await ProcessAsync(new LocalSendServer(), "GET /missing HTTP/1.1\r\nHost: test\r\n\r\n");

        StringAssert.Contains(response, "Content-Type: text/plain; charset=utf-8");
        StringAssert.Contains(response, "Not found");
    }

    [TestMethod]
    public async Task ProcessAsync_ShowRequest_WithValidToken_ReturnsSuccess()
    {
        var server = new LocalSendServer { ShowToken = "token" };
        const string request = "POST /api/localsend/v2/show?token=token HTTP/1.1\r\nContent-Length: 0\r\n\r\n";

        var response = await ProcessAsync(server, request);

        StringAssert.Contains(response, "HTTP/1.1 200 OK");
    }

    [TestMethod]
    public async Task ProcessAsync_ChunkedRegistration_ParsesRequestBody()
    {
        const string body = "{\"alias\":\"Sender\",\"fingerprint\":\"fingerprint\"}";
        var request = $"POST /api/localsend/v2/register HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n{body.Length:X}\r\n{body}\r\n0\r\n\r\n";

        var response = await ProcessAsync(new LocalSendServer(), request);

        StringAssert.Contains(response, "HTTP/1.1 200 OK");
    }

    [TestMethod]
    public async Task ProcessAsync_LargeContentLength_UsesA64BitLength()
    {
        const string body = "{\"alias\":\"Sender\",\"fingerprint\":\"fingerprint\"}";
        var request = $"POST /api/localsend/v2/register HTTP/1.1\r\nContent-Length: 2147483648\r\n\r\n{body}";

        var response = await ProcessAsync(new LocalSendServer(), request);

        StringAssert.Contains(response, "HTTP/1.1 200 OK");
    }

    private static async Task<string> ProcessAsync(LocalSendServer server, string request)
    {
        await using var stream = new MemoryStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(request));
        stream.Position = 0;
        await LocalSendServerHandler.ProcessAsync(server, stream, new IPEndPoint(IPAddress.Parse("192.168.1.20"), 12345), CancellationToken.None);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
