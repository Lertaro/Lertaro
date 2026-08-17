using System.Text;
using Lertaro.Core.Extensions;

namespace Lertaro.Core.Wire;

public static class PipeRequestBinarySerializer
{
    private const int Magic = 0x51504C53; // SLPQ

    private const int VersionString = 1;
    private const int VersionIpc = 2;

    public static Task WriteStringAsync(Stream stream, string command, CancellationToken token = default)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
            writer.Write(command ?? string.Empty);
        return WriteFrameAsync(stream, VersionString, payload.ToArray(), token);
    }

    public static async Task<string> ReadStringAsync(Stream stream, CancellationToken token = default)
    {
        var payload = await ReadFrameAsync(stream, VersionString, token).ConfigureAwait(false);
        using var ms = new MemoryStream(payload);
        using var reader = new BinaryReader(ms, Encoding.UTF8);
        return reader.ReadString();
    }

    public static Task WriteMessageAsync(Stream stream, IpcMessage msg, CancellationToken token = default)
    {
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
            WritePayload(writer, msg);
        return WriteFrameAsync(stream, VersionIpc, payload.ToArray(), token);
    }

    public static async Task<IpcMessage> ReadMessageAsync(Stream stream, CancellationToken token = default)
    {
        var payload = await ReadFrameAsync(stream, VersionIpc, token).ConfigureAwait(false);
        using var ms = new MemoryStream(payload);
        using var reader = new BinaryReader(ms, Encoding.UTF8);
        return ReadPayload(reader);
    }

    private static void WritePayload(BinaryWriter writer, IpcMessage msg)
    {
        writer.Write((byte)msg.Id);
        switch (msg.Id)
        {
            case IpcMessageId.Stop:
            case IpcMessageId.ReloadSettings:
            case IpcMessageId.RequestOpenedFolders:
            case IpcMessageId.Activate:
            case IpcMessageId.ExplorerDeactivated:
            case IpcMessageId.ActiveWindowMoved:
            case IpcMessageId.KeyBackspace:
            case IpcMessageId.KeyEscape:
            case IpcMessageId.KeyEnter:
            case IpcMessageId.KeyUp:
            case IpcMessageId.QuickPanelHotkey:
            case IpcMessageId.KeyDown:
            case IpcMessageId.KeyLeft:
            case IpcMessageId.KeyRight:
                break;

            case IpcMessageId.SetAppProcessId:
            case IpcMessageId.KillProcess:
                writer.Write(msg.ProcessId);
                break;

            case IpcMessageId.SetQuickSearchVisible:
            case IpcMessageId.SetInlineSearchVisible:
            case IpcMessageId.SetInlineWindowOnScreen:
            case IpcMessageId.SetHotkeysDisabled:
                writer.Write(msg.BoolVal);
                break;

            case IpcMessageId.NavigateDialog:
                writer.Write(msg.Hwnd);
                writer.Write(msg.StringVal1 ?? string.Empty);
                break;

            case IpcMessageId.RestoreDialogFocus:
                writer.Write(msg.Hwnd);
                break;

            case IpcMessageId.ForceForeground:
                writer.Write(msg.Hwnd);
                writer.Write(msg.BoolVal);
                break;

            case IpcMessageId.KeyChar:
                writer.Write(msg.CharVal);
                break;

            case IpcMessageId.KeyCtrlNumber:
                writer.Write(msg.IntVal);
                break;

            case IpcMessageId.MouseClick:
            case IpcMessageId.MouseDoubleClick:
            case IpcMessageId.MouseMiddleClick:
                writer.Write(msg.MouseX);
                writer.Write(msg.MouseY);
                break;

            case IpcMessageId.ExplorerActivated:
                writer.Write(msg.Hwnd);
                writer.Write(msg.StringVal1 ?? string.Empty);
                writer.Write(msg.StringVal2 ?? string.Empty);
                writer.Write(msg.IsDesktop);
                break;

            case IpcMessageId.PathCaptured:
            case IpcMessageId.Error:
                writer.Write(msg.StringVal1 ?? string.Empty);
                if (msg.Id == IpcMessageId.PathCaptured)
                    writer.Write(msg.IsDesktop);
                break;

            case IpcMessageId.ExecuteInlineItem:
                writer.Write(msg.Hwnd);
                writer.Write(msg.StringVal1 ?? string.Empty);
                writer.Write(msg.StringVal2 ?? string.Empty);
                writer.Write(msg.IntVal);
                break;

            case IpcMessageId.InlineSelectionChanged:
                writer.Write(msg.Hwnd);
                writer.Write(msg.StringVal1 ?? string.Empty);
                break;

            case IpcMessageId.InlineSearchFinished:
                writer.Write(msg.Hwnd);
                writer.Write(msg.BoolVal);
                break;

            case IpcMessageId.ExecuteInlineItemResponse:
                writer.Write(msg.IntVal);
                writer.Write(msg.BoolVal);
                break;

            case IpcMessageId.OpenedFoldersCaptured:
                var paths = msg.StringList ?? Array.Empty<string>();
                writer.Write(paths.Count);
                foreach (var path in paths)
                    writer.Write(path);
                break;
        }
    }

    private static IpcMessage ReadPayload(BinaryReader reader)
    {
        var msg = new IpcMessage { Id = (IpcMessageId)reader.ReadByte() };
        switch (msg.Id)
        {
            case IpcMessageId.Stop:
            case IpcMessageId.ReloadSettings:
            case IpcMessageId.RequestOpenedFolders:
            case IpcMessageId.Activate:
            case IpcMessageId.ExplorerDeactivated:
            case IpcMessageId.ActiveWindowMoved:
            case IpcMessageId.KeyBackspace:
            case IpcMessageId.KeyEscape:
            case IpcMessageId.KeyEnter:
            case IpcMessageId.KeyUp:
            case IpcMessageId.QuickPanelHotkey:
            case IpcMessageId.KeyDown:
            case IpcMessageId.KeyLeft:
            case IpcMessageId.KeyRight:
                break;

            case IpcMessageId.SetAppProcessId:
            case IpcMessageId.KillProcess:
                msg.ProcessId = reader.ReadUInt32();
                break;

            case IpcMessageId.SetQuickSearchVisible:
            case IpcMessageId.SetInlineSearchVisible:
            case IpcMessageId.SetInlineWindowOnScreen:
            case IpcMessageId.SetHotkeysDisabled:
                msg.BoolVal = reader.ReadBoolean();
                break;

            case IpcMessageId.NavigateDialog:
                msg.Hwnd = reader.ReadInt64();
                msg.StringVal1 = reader.ReadString();
                break;

            case IpcMessageId.RestoreDialogFocus:
                msg.Hwnd = reader.ReadInt64();
                break;

            case IpcMessageId.ForceForeground:
                msg.Hwnd = reader.ReadInt64();
                msg.BoolVal = reader.ReadBoolean();
                break;

            case IpcMessageId.KeyChar:
                msg.CharVal = reader.ReadChar();
                break;

            case IpcMessageId.KeyCtrlNumber:
                msg.IntVal = reader.ReadInt32();
                break;

            case IpcMessageId.MouseClick:
            case IpcMessageId.MouseDoubleClick:
            case IpcMessageId.MouseMiddleClick:
                msg.MouseX = reader.ReadInt32();
                msg.MouseY = reader.ReadInt32();
                break;

            case IpcMessageId.ExplorerActivated:
                msg.Hwnd = reader.ReadInt64();
                msg.StringVal1 = reader.ReadString();
                msg.StringVal2 = reader.ReadString();
                msg.IsDesktop = reader.ReadBoolean();
                break;

            case IpcMessageId.PathCaptured:
                msg.StringVal1 = reader.ReadString();
                msg.IsDesktop = reader.ReadBoolean();
                break;

            case IpcMessageId.Error:
                msg.StringVal1 = reader.ReadString();
                break;

            case IpcMessageId.ExecuteInlineItem:
                msg.Hwnd = reader.ReadInt64();
                msg.StringVal1 = reader.ReadString();
                msg.StringVal2 = reader.ReadString();
                msg.IntVal = reader.ReadInt32();
                break;

            case IpcMessageId.InlineSelectionChanged:
                msg.Hwnd = reader.ReadInt64();
                msg.StringVal1 = reader.ReadString();
                break;

            case IpcMessageId.InlineSearchFinished:
                msg.Hwnd = reader.ReadInt64();
                msg.BoolVal = reader.ReadBoolean();
                break;

            case IpcMessageId.ExecuteInlineItemResponse:
                msg.IntVal = reader.ReadInt32();
                msg.BoolVal = reader.ReadBoolean();
                break;

            case IpcMessageId.OpenedFoldersCaptured:
                var count = reader.ReadInt32();
                if (count < 0 || count > 10_000)
                    throw new InvalidDataException("Opened-folder snapshot exceeds the IPC limit.");
                var paths = new string[count];
                for (var index = 0; index < count; index++)
                    paths[index] = reader.ReadString();
                msg.StringList = paths;
                break;
        }

        return msg;
    }

    private static async Task WriteFrameAsync(Stream stream, int version, byte[] payload, CancellationToken token)
    {
        using var frame = new MemoryStream();
        using (var writer = new BinaryWriter(frame, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(version);
            writer.Write(payload.Length);
            writer.Write(payload);
        }

        await stream.WriteAsync(frame.ToArray(), token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadFrameAsync(Stream stream, int expectedVersion, CancellationToken token)
    {
        var magic = await stream.ReadInt32Async(token).ConfigureAwait(false);
        if (magic != Magic)
            throw new InvalidDataException("Invalid pipe request binary header.");
        var version = await stream.ReadInt32Async(token).ConfigureAwait(false);
        if (version != expectedVersion)
            throw new InvalidDataException($"Unsupported pipe request version: {version}.");
        var length = await stream.ReadInt32Async(token).ConfigureAwait(false);
        if (length < 0 || length > 10 * 1024 * 1024)
            throw new InvalidDataException($"Invalid IPC payload length: {length}");
        return await stream.ReadExactlyAsync(length, token).ConfigureAwait(false);
    }
}
