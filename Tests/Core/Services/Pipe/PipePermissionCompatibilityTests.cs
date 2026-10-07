using System.IO.Pipes;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Services;
using Lertaro.Core.Services.Search;
using Lertaro.Core.Wire;
using Lertaro.Core.Tests.IndexV2;

namespace Lertaro.Core.Tests.Services.Pipe;

[TestClass]
public sealed class PipePermissionCompatibilityTests
{
    [TestMethod]
    public async Task ServiceLogResponse_PreservesMultilineUnicodeText()
    {
        const string message = "[Warn] 私有目录\n[Info] C:\\Users\\示例\\文档.txt";
        using var stream = new MemoryStream();
        await PipeResponseBinarySerializer.WriteAsync(stream, new PipeResponse { Kind = PipeResponseKind.ServiceLog, Message = message }, default);
        stream.Position = 0;
        var response = await PipeResponseBinarySerializer.ReadAsync(stream);
        Assert.AreEqual(PipeResponseKind.ServiceLog, response.Kind);
        Assert.AreEqual(message, response.Message);
    }

    [TestMethod]
    public async Task StopService_RejectsUnknownProcessAfterRealWireRoundTrip()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var name = "LertaroStopTest_" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        var write = SearchRequestBinarySerializer.WriteSearchRequestAsync(client, new SearchRequestMessage { Id = SearchRequestId.StopService }, timeout.Token);
        var request = await SearchRequestBinarySerializer.ReadSearchRequestAsync(server, timeout.Token);
        await write;
        Assert.AreEqual(SearchRequestId.StopService, request.Id);
        var stopped = false;
        var handle = UsnServicePipeServer.HandleStopRequestAsync(server, () => stopped = true, timeout.Token);
        var response = await PipeResponseBinarySerializer.ReadAsync(client, timeout.Token);
        await handle;
        Assert.AreEqual(PipeResponseKind.Error, response.Kind);
        Assert.IsFalse(stopped);
    }

    [TestMethod]
    public async Task SpaceTotals_UseIndexedSizesWhileListedPathsReflectAclChanges()
    {
        var folder = Directory.CreateTempSubdirectory("LertaroSpaceAccess_").FullName;
        var hidden = Directory.CreateDirectory(Path.Combine(folder, "private")).FullName;
        File.WriteAllBytes(Path.Combine(folder, "shared.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(hidden, "private.bin"), new byte[900]);
        var original = new DirectoryInfo(hidden).GetAccessControl();
        original.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
        var denied = new DirectoryInfo(hidden).GetAccessControl();
        denied.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny));
        new DirectoryInfo(hidden).SetAccessControl(denied);
        using var fixture = LiveIndexFixture.Build(folder, [LiveIndexFixture.Root(),
            new FileRecord(2, 1, "private", FileRecordFlags.Directory),
            new FileRecord(3, 1, "shared.bin", FileRecordFlags.None, 100),
            new FileRecord(4, 2, "private.bin", FileRecordFlags.None, 900)]);
        using var engine = new SearchEngine();
        var indexer = (UsnIndexer)typeof(SearchEngine).GetField("_indexer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
        indexer._recordIndexes.Add(folder, fixture.Index);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var name = "LertaroSpaceTest_" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        try
        {
            await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
            await Task.WhenAll(client.WriteAsync(new byte[] { 1 }, timeout.Token).AsTask(), server.ReadExactlyAsync(new byte[1], timeout.Token).AsTask());
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => File.ReadAllBytes(Path.Combine(hidden, "private.bin")));
            using var visibility = CallerVisibility.ForClient(server);
            var request = new SearchRequestMessage { Id = SearchRequestId.GetSpaceEntries };
            var response = await UsnServicePipeRequestProcessor.ProcessAsync(engine, request, timeout.Token, server, visibility);
            Assert.AreEqual(1000L, response.SpaceEntries!.Single().Size,
                "Home totals describe indexed content without opening every descendant.");
            request.Drive = folder;
            response = await UsnServicePipeRequestProcessor.ProcessAsync(engine, request, timeout.Token, server, visibility);
            Assert.AreEqual("shared.bin", response.SpaceEntries!.Single().Name);
            request.Drive = hidden;
            response = await UsnServicePipeRequestProcessor.ProcessAsync(engine, request, timeout.Token, server, visibility);
            Assert.IsEmpty(response.SpaceEntries!, "A denied directory cannot be browsed directly.");

            new DirectoryInfo(hidden).SetAccessControl(original);
            request.Drive = folder;
            response = await UsnServicePipeRequestProcessor.ProcessAsync(engine, request, timeout.Token, server, visibility);
            Assert.HasCount(2, response.SpaceEntries!);
            Assert.AreEqual(1000L, response.SpaceEntries!.Sum(entry => entry.Size));

            denied.SetSecurityDescriptorBinaryForm(denied.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            new DirectoryInfo(hidden).SetAccessControl(denied);
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => File.ReadAllBytes(Path.Combine(hidden, "private.bin")));
            response = await UsnServicePipeRequestProcessor.ProcessAsync(engine, request, timeout.Token, server, visibility);
            Assert.AreEqual("shared.bin", response.SpaceEntries!.Single().Name,
                "A cached directory listing must apply current ACLs again after access is revoked.");
        }
        finally
        {
            indexer._recordIndexes.Remove(folder);
            original.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
            new DirectoryInfo(hidden).SetAccessControl(original);
            Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    public async Task Visibility_UsesCallersRealFileAccessOutsideProfileRoots()
    {
        var folder = Directory.CreateTempSubdirectory("LertaroPipeAccess_").FullName;
        var path = Path.Combine(folder, "private.txt");
        File.WriteAllText(path, "private");
        var acl = new FileInfo(path).GetAccessControl();
        var original = new FileInfo(path).GetAccessControl();
        original.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
        acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData, AccessControlType.Deny));
        new FileInfo(path).SetAccessControl(acl);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var name = "LertaroPermissionTest_" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        try
        {
            await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
            await Task.WhenAll(client.WriteAsync(new byte[] { 1 }, timeout.Token).AsTask(),
                server.ReadExactlyAsync(new byte[1], timeout.Token).AsTask());
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => File.ReadAllText(path));

            using var visibility = CallerVisibility.ForClient(server);
            Assert.IsFalse(visibility.IsVisible(path));
            new FileInfo(path).SetAccessControl(original);
            Assert.AreEqual("private", File.ReadAllText(path));
            Assert.IsTrue(visibility.IsVisible(path), "Permission changes must not remain cached on a long-lived connection.");
        }
        finally
        {
            new FileInfo(path).SetAccessControl(original);
            Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    public async Task Notification_ParentSubscription_OnlySendsVisibleActualDirectories()
    {
        var folder = Directory.CreateTempSubdirectory("LertaroNotification_").FullName;
        var hidden = Directory.CreateDirectory(Path.Combine(folder, "private")).FullName;
        var visible = Directory.CreateDirectory(Path.Combine(folder, "shared")).FullName;
        File.WriteAllText(Path.Combine(hidden, "private.txt"), "private");
        File.WriteAllText(Path.Combine(visible, "shared.txt"), "shared");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var name = "LertaroNotificationTest_" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var engine = new SearchEngine();
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        var subscription = DirectoryChangeSubscription.ServeAsync(server, engine, [folder], new CallerVisibility([hidden]), timeout.Token);
        try
        {
            var indexer = (UsnIndexer)typeof(SearchEngine).GetField("_indexer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
            indexer.RaiseDirectoriesChanged(Path.GetPathRoot(folder)!, [hidden, visible]);
            var response = await PipeResponseBinarySerializer.ReadAsync(client, timeout.Token);
            CollectionAssert.AreEqual(new[] { visible }, response.ChangedDirectories!);
        }
        finally
        {
            timeout.Cancel();
            await subscription;
            Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    [DataRow(SearchRequestId.Initialize)]
    [DataRow(SearchRequestId.ClearPathCaches)]
    [DataRow(SearchRequestId.GetServiceLog)]
    public async Task GlobalControls_RejectUnknownProcess(SearchRequestId command)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var name = "LertaroControlTest_" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        await Task.WhenAll(client.WriteAsync(new byte[] { 1 }, timeout.Token).AsTask(),
            server.ReadExactlyAsync(new byte[1], timeout.Token).AsTask());
        var response = UsnServicePipeRequestProcessor.Process(null, new SearchRequestMessage { Id = command }, timeout.Token, server, CallerVisibility.Everything);
        Assert.AreEqual(PipeResponseKind.Error, response.Kind);
    }
}
