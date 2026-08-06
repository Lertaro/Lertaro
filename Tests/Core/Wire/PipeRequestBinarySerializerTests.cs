using Lertaro.Core.Wire;

namespace Lertaro.Core.Tests.Wire;

[TestClass]
public sealed class PipeRequestBinarySerializerTests
{
    [TestMethod]
    public async Task WriteStringAsync_ThenReadStringAsync_RoundTrips()
    {
        using var stream = new MemoryStream();
        await PipeRequestBinarySerializer.WriteStringAsync(stream, "ping");
        stream.Position = 0;

        var result = await PipeRequestBinarySerializer.ReadStringAsync(stream);

        Assert.AreEqual("ping", result);
    }

    [TestMethod]
    public async Task WriteMessageAsync_SimpleMessage_RoundTripsId()
    {
        using var stream = new MemoryStream();
        var message = new IpcMessage { Id = IpcMessageId.Stop };
        await PipeRequestBinarySerializer.WriteMessageAsync(stream, message);
        stream.Position = 0;

        var result = await PipeRequestBinarySerializer.ReadMessageAsync(stream);

        Assert.AreEqual(IpcMessageId.Stop, result.Id);
    }

    [TestMethod]
    public async Task WriteMessageAsync_MessageWithHwndAndString_RoundTripsAllFields()
    {
        using var stream = new MemoryStream();
        var message = new IpcMessage
        {
            Id = IpcMessageId.NavigateDialog,
            Hwnd = 0x1234ABCD,
            StringVal1 = @"C:\Users\test"
        };
        await PipeRequestBinarySerializer.WriteMessageAsync(stream, message);
        stream.Position = 0;

        var result = await PipeRequestBinarySerializer.ReadMessageAsync(stream);

        Assert.AreEqual(IpcMessageId.NavigateDialog, result.Id);
        Assert.AreEqual(0x1234ABCD, result.Hwnd);
        Assert.AreEqual(@"C:\Users\test", result.StringVal1);
    }

    [TestMethod]
    public async Task WriteMessageAsync_MouseMessage_RoundTripsCoordinates()
    {
        using var stream = new MemoryStream();
        var message = new IpcMessage { Id = IpcMessageId.MouseClick, MouseX = 100, MouseY = -50 };
        await PipeRequestBinarySerializer.WriteMessageAsync(stream, message);
        stream.Position = 0;

        var result = await PipeRequestBinarySerializer.ReadMessageAsync(stream);

        Assert.AreEqual(100, result.MouseX);
        Assert.AreEqual(-50, result.MouseY);
    }

    [TestMethod]
    public async Task WriteMessageAsync_ExplorerActivated_RoundTripsAllFields()
    {
        using var stream = new MemoryStream();
        var message = new IpcMessage
        {
            Id = IpcMessageId.ExplorerActivated,
            Hwnd = 42,
            StringVal1 = "explorer.exe",
            StringVal2 = @"C:\Windows",
            IsDesktop = true
        };
        await PipeRequestBinarySerializer.WriteMessageAsync(stream, message);
        stream.Position = 0;

        var result = await PipeRequestBinarySerializer.ReadMessageAsync(stream);

        Assert.AreEqual(42, result.Hwnd);
        Assert.AreEqual("explorer.exe", result.StringVal1);
        Assert.AreEqual(@"C:\Windows", result.StringVal2);
        Assert.IsTrue(result.IsDesktop);
    }

    [TestMethod]
    public async Task WriteMessageAsync_MultipleMessages_RoundTripInOrderOnSameStream()
    {
        using var stream = new MemoryStream();
        await PipeRequestBinarySerializer.WriteMessageAsync(stream, new IpcMessage { Id = IpcMessageId.KeyEnter });
        await PipeRequestBinarySerializer.WriteMessageAsync(stream, new IpcMessage { Id = IpcMessageId.KeyEscape });
        stream.Position = 0;

        var first = await PipeRequestBinarySerializer.ReadMessageAsync(stream);
        var second = await PipeRequestBinarySerializer.ReadMessageAsync(stream);

        Assert.AreEqual(IpcMessageId.KeyEnter, first.Id);
        Assert.AreEqual(IpcMessageId.KeyEscape, second.Id);
    }

    [TestMethod]
    public async Task ReadMessageAsync_CorruptedMagicHeader_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => PipeRequestBinarySerializer.ReadMessageAsync(stream));
    }

    [TestMethod]
    public async Task WriteMessageAsync_QuickPanelHotkey_RoundTripsId()
    {
        // An id the writer's switch does not name is written as a bare header and read back as one, so a
        // message added without its serializer arm still round-trips its Id and only loses its payload.
        // This one carries none, which is exactly why the omission would be invisible: it would look
        // like it worked. Pinned here so the arm cannot be dropped later, when the message may not be
        // empty any more.
        using var stream = new MemoryStream();
        await PipeRequestBinarySerializer.WriteMessageAsync(
            stream, new IpcMessage { Id = IpcMessageId.QuickPanelHotkey });
        stream.Position = 0;

        var result = await PipeRequestBinarySerializer.ReadMessageAsync(stream);

        Assert.AreEqual(IpcMessageId.QuickPanelHotkey, result.Id);
    }

    [TestMethod]
    public void EveryMessageId_IsNamedByBothSerializerSwitches()
    {
        // The real guard, and the one that would have caught a forgotten arm: a payload-carrying id
        // missing from the writer's switch serializes empty and reads back with default values, which
        // no round-trip of that id alone would reveal.
        var writer = File.ReadAllText(SerializerSource());

        var unnamed = Enum.GetValues<IpcMessageId>()
            .Select(id => id.ToString())
            .Where(name => !writer.Contains($"IpcMessageId.{name}:", StringComparison.Ordinal))
            .ToList();

        Assert.IsEmpty(unnamed,
            "these message ids appear in no case arm of PipeRequestBinarySerializer, so their payloads "
            + "are silently dropped: " + string.Join(", ", unnamed));
    }

    private static string SerializerSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");

        var path = Path.Combine(dir!.FullName, "Core", "Wire", "PipeRequestBinarySerializer.cs");
        Assert.IsTrue(File.Exists(path), $"expected the serializer at {path}");
        return path;
    }
}
