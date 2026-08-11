using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;
using System.Net;
using System.Net.Sockets;

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

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
