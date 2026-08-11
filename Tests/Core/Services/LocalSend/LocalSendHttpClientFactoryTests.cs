using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Lertaro.Core.Tests.Services.LocalSend;

[TestClass]
[DoNotParallelize]
public sealed class LocalSendHttpClientFactoryTests
{
    [TestMethod]
    public void NormalizeFingerprint_RemovesSeparatorsAndNormalizesCase() => Assert.AreEqual("AABBCC", LocalSendHttpClientFactory.NormalizeFingerprint("aa:bb:cc"));

    [TestMethod]
    public void CreateEphemeral_ContainsNoUserOrMachineIdentity()
    {
        using var certificate = LocalSendCertificate.CreateEphemeral();

        Assert.AreEqual("CN=LocalSend", certificate.Subject);
        Assert.IsFalse(certificate.Extensions.OfType<System.Security.Cryptography.X509Certificates.X509SubjectAlternativeNameExtension>().Any());
        Assert.IsTrue(LocalSendCertificate.IsValidPeerCertificate(certificate));
    }

    [TestMethod]
    public async Task Create_WithClientCertificateAndMatchingPin_CompletesHttpsRequest()
    {
        using var serverCertificate = LocalSendCertificate.CreateEphemeral();
        using var clientCertificate = LocalSendCertificate.CreateEphemeral();
        var port = GetFreePort();
        using var server = new LocalSendServer
        {
            Certificate = serverCertificate,
            DeviceInfo = new LocalSendDeviceInfo
            {
                Alias = "Server", Protocol = "https",
                Fingerprint = LocalSendCertificate.GetFingerprint(serverCertificate)
            }
        };
        server.Start(port);
        using var client = LocalSendHttpClientFactory.Create(
            clientCertificate, LocalSendCertificate.GetFingerprint(serverCertificate), TimeSpan.FromSeconds(3));

        using var response = await client.GetAsync($"https://127.0.0.1:{port}/api/localsend/v2/info");

        Assert.IsTrue(response.IsSuccessStatusCode);
    }

    [TestMethod]
    public async Task Create_WithMismatchedPin_RejectsPeerBeforeRequestCompletes()
    {
        using var serverCertificate = LocalSendCertificate.CreateEphemeral();
        using var clientCertificate = LocalSendCertificate.CreateEphemeral();
        var port = GetFreePort();
        using var server = new LocalSendServer
        {
            Certificate = serverCertificate,
            DeviceInfo = new LocalSendDeviceInfo
            {
                Alias = "Server", Protocol = "https",
                Fingerprint = LocalSendCertificate.GetFingerprint(serverCertificate)
            }
        };
        server.Start(port);
        using var client = LocalSendHttpClientFactory.Create(
            clientCertificate, new string('0', 64), TimeSpan.FromSeconds(3));

        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => client.GetAsync($"https://127.0.0.1:{port}/api/localsend/v2/info"));
    }

    [TestMethod]
    public async Task PrepareUpload_ClientAbortsHttpsRequest_CancelsPendingSession()
    {
        using var serverCertificate = LocalSendCertificate.CreateEphemeral();
        using var clientCertificate = LocalSendCertificate.CreateEphemeral();
        var port = GetFreePort();
        using var server = new LocalSendServer
        {
            Certificate = serverCertificate,
            DeviceInfo = new LocalSendDeviceInfo
            {
                Alias = "Server", Protocol = "https",
                Fingerprint = LocalSendCertificate.GetFingerprint(serverCertificate)
            }
        };
        var requestShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionCanceled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.UploadRequested += (_, _) => requestShown.TrySetResult();
        server.SessionCanceled += (_, sessionId) => sessionCanceled.TrySetResult(sessionId);
        server.Start(port);
        using var client = LocalSendHttpClientFactory.Create(
            clientCertificate, LocalSendCertificate.GetFingerprint(serverCertificate), TimeSpan.FromSeconds(3));
        using var cancellation = new CancellationTokenSource();
        const string body = """
            {"info":{"alias":"Sender","version":"2.2"},"files":{"file":{"id":"file","fileName":"test.txt","size":0,"fileType":"text/plain"}}}
            """;

        var sending = client.PostAsync($"https://127.0.0.1:{port}/api/localsend/v2/prepare-upload",
            new StringContent(body, Encoding.UTF8, "application/json"), cancellation.Token);
        await requestShown.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(async () => await sending);
        Assert.IsFalse(string.IsNullOrEmpty(await sessionCanceled.Task.WaitAsync(TimeSpan.FromSeconds(3))));
        Assert.IsFalse(server.HasActiveSessions);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
