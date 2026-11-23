using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Lertaro.App.ViewModels.LocalSend;
using Lertaro.Core.Services.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.App.Tests.ViewModels.LocalSend;

[TestClass]
[DoNotParallelize]
public sealed class LocalSendSendViewModelTests
{
    [TestInitialize]
    public void Initialize() => LocalSendServiceManager.Instance.Stop();

    [TestCleanup]
    public void Cleanup() => LocalSendServiceManager.Instance.Stop();

    [TestMethod]
    public void SetMode_WithText_SwitchesExistingItemRequestToTextDeviceSelection()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var viewModel = new LocalSendSendViewModel(new[] { path });

            viewModel.SetMode(LocalSendSendMode.Text, "hello", proceed: true);

            Assert.IsTrue(viewModel.IsTextMode);
            Assert.AreEqual("hello", viewModel.TextToSend);
            Assert.IsEmpty(viewModel.TargetFiles);
            Assert.AreEqual(1, viewModel.CurrentStep);
            Assert.IsTrue(viewModel.IsFromAction);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Constructor_WithEmptyTextMode_OpensTextCollectionStep()
    {
        using var viewModel = new LocalSendSendViewModel(initialMode: LocalSendSendMode.Text);

        Assert.IsTrue(viewModel.IsTextMode);
        Assert.AreEqual(0, viewModel.CurrentStep);
        Assert.IsFalse(viewModel.CanGoNextStep);
    }

    [TestMethod]
    public void SetMode_WithEmptyText_ClearsPreviousText()
    {
        using var viewModel = new LocalSendSendViewModel(initialText: "previous");

        viewModel.SetMode(LocalSendSendMode.Text);

        Assert.AreEqual(string.Empty, viewModel.TextToSend);
        Assert.AreEqual(0, viewModel.CurrentStep);
        Assert.IsFalse(viewModel.CanGoNextStep);
    }

    [TestMethod]
    public void ReturnToStep0_ClearsThePreviousResultAndKeepsTheTextForAnotherSend()
    {
        using var viewModel = new LocalSendSendViewModel(initialText: "retained text");
        viewModel.CurrentStep = 2;
        viewModel.LastSendResult = LocalSendSendResult.ReceiverCanceled;
        viewModel.TransferItems.Add(new LocalSendSendFileItem
        {
            DisplayName = "message", Size = 1, SizeText = string.Empty, IsConfirmed = true
        });

        viewModel.ReturnToStep0();

        Assert.AreEqual(0, viewModel.CurrentStep);
        Assert.IsFalse(viewModel.IsFromAction);
        Assert.IsEmpty(viewModel.TransferItems);
        Assert.AreEqual("(0/0)", viewModel.ConfirmedCounterText);
        Assert.IsEmpty(viewModel.SpeedText);
        Assert.AreEqual(LocalSendSendResult.Success, viewModel.LastSendResult);
        Assert.AreEqual("retained text", viewModel.TextToSend);
        Assert.IsTrue(viewModel.CanGoNextStep);
    }

    [TestMethod]
    public void ReturnToStep0_PreservesCollectedFiles()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var viewModel = new LocalSendSendViewModel([path]);
            viewModel.CurrentStep = 2;

            viewModel.ReturnToStep0();
            viewModel.ProceedToStep1();

            Assert.HasCount(1, viewModel.CollectedItems);
            Assert.HasCount(1, viewModel.TargetFiles);
            Assert.AreEqual(path, viewModel.TargetFiles[0]);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task ReturnToStep0_WaitsForRecipientConfirmationAndAllowsASecondBatch()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var device = new LocalSendSendDeviceItem(new LocalSendDeviceInfo
        {
            Alias = "Receiver", IpAddress = "127.0.0.1", Port = ((IPEndPoint)listener.LocalEndpoint).Port,
            Protocol = "http", Version = "2.2"
        });
        using var viewModel = new LocalSendSendViewModel(initialText: "hello");
        viewModel.CurrentStep = 2;
        var first = viewModel.StartSendBatchAsync([device]);
        using (var peer = await listener.AcceptTcpClientAsync(timeout.Token))
        {
            await ReadPrepareRequestAsync(peer.GetStream(), timeout.Token);
            viewModel.ReturnToStep0();
            Assert.IsTrue(viewModel.IsSending);
            Assert.AreEqual(2, viewModel.CurrentStep);
            Assert.IsFalse(first.IsCompleted);
            await ReplyReadAndClosedAsync(peer.GetStream(), timeout.Token);
            await first.WaitAsync(timeout.Token);
        }

        Assert.AreEqual(LocalSendSendResult.Success, viewModel.LastSendResult);
        viewModel.ReturnToStep0();
        Assert.AreEqual(0, viewModel.CurrentStep);
        Assert.IsEmpty(viewModel.TransferItems);
        viewModel.ProceedToStep1();
        viewModel.CurrentStep = 2;
        var second = viewModel.StartSendBatchAsync([device]);
        using (var peer = await listener.AcceptTcpClientAsync(timeout.Token))
        {
            await ReadPrepareRequestAsync(peer.GetStream(), timeout.Token);
            await ReplyReadAndClosedAsync(peer.GetStream(), timeout.Token);
            await second.WaitAsync(timeout.Token);
        }
        Assert.IsFalse(viewModel.IsSending);
        Assert.AreEqual(LocalSendSendResult.Success, viewModel.LastSendResult);
        Assert.AreEqual("(1/1)", viewModel.ConfirmedCounterText);
    }

    private static async Task ReadPrepareRequestAsync(NetworkStream stream, CancellationToken token)
    {
        var header = new List<byte>();
        var single = new byte[1];
        while (header.Count < 16384)
        {
            await stream.ReadExactlyAsync(single, token);
            header.Add(single[0]);
            if (header.Count >= 4 && header[^4] == '\r' && header[^3] == '\n'
                && header[^2] == '\r' && header[^1] == '\n') break;
        }
        var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
        Assert.StartsWith("POST /api/localsend/v2/prepare-upload ", lines[0]);
        var length = int.Parse(lines.Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':')[1]);
        await stream.ReadExactlyAsync(new byte[length], token);
    }

    private static ValueTask ReplyReadAndClosedAsync(NetworkStream stream, CancellationToken token) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), token);
}
