using System.Buffers.Binary;
using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.Wire;

// IndexerStatus-specific (de)serialization for PipeResponseBinarySerializer -- split out to keep
// that file under the repo's line-count limit.
internal static class PipeResponseStatusSerializer
{
    public static int CalculateSize(UsnIndexer.IndexerStatus status)
    {
        var size = PipeResponseBinarySerializer.GetStringByteCount(status.State) + 5;
        size += 21; // Progress(4) + TotalFiles(4) + TotalDirs(4) + ElapsedTime(8) + IsMaintenanceBusy(1)
        size += 4;  // ActiveDrives count
        foreach (var drive in status.ActiveDrives)
            size += PipeResponseBinarySerializer.GetStringByteCount(drive) + 5;

        size += 4;  // Drives count
        foreach (var drive in status.Drives)
        {
            size += PipeResponseBinarySerializer.GetStringByteCount(drive.Drive) + 5;
            size += 1; // Enabled
            size += PipeResponseBinarySerializer.GetStringByteCount(drive.Kind) + 5;
            size += PipeResponseBinarySerializer.GetStringByteCount(drive.State) + 5;
            size += 8; // Files(4) + Dirs(4)
            size += PipeResponseBinarySerializer.GetStringByteCount(drive.CachePath) + 5;
        }
        return size;
    }

    public static void Write(Span<byte> span, ref int offset, UsnIndexer.IndexerStatus status)
    {
        PipeResponseBinarySerializer.WriteString(span, ref offset, status.State);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), status.Progress);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), status.TotalFiles);
        offset += 4;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), status.TotalDirs);
        offset += 4;
        BinaryPrimitives.WriteDoubleLittleEndian(span.Slice(offset), status.ElapsedTime);
        offset += 8;
        span[offset++] = (byte)(status.IsMaintenanceBusy ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), status.ActiveDrives.Count);
        offset += 4;
        foreach (var drive in status.ActiveDrives)
            PipeResponseBinarySerializer.WriteString(span, ref offset, drive);

        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), status.Drives.Count);
        offset += 4;
        foreach (var drive in status.Drives)
        {
            PipeResponseBinarySerializer.WriteString(span, ref offset, drive.Drive);
            span[offset++] = (byte)(drive.Enabled ? 1 : 0);
            PipeResponseBinarySerializer.WriteString(span, ref offset, drive.Kind);
            PipeResponseBinarySerializer.WriteString(span, ref offset, drive.State);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), drive.Files);
            offset += 4;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset), drive.Dirs);
            offset += 4;
            PipeResponseBinarySerializer.WriteString(span, ref offset, drive.CachePath);
        }
    }

    public static UsnIndexer.IndexerStatus Read(byte[] payload, ref int offset)
    {
        var status = new UsnIndexer.IndexerStatus
        {
            State = PipeResponseBinarySerializer.ReadString(payload, ref offset),
            Progress = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset)),
            TotalFiles = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 4)),
            TotalDirs = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 8)),
            ElapsedTime = BinaryPrimitives.ReadDoubleLittleEndian(payload.AsSpan(offset + 12)),
            IsMaintenanceBusy = payload[offset + 20] != 0
        };
        offset += 21;

        var activeCount = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        offset += 4;
        if (activeCount < 0 || activeCount > payload.Length - offset)
            throw new InvalidDataException("Invalid active drive count.");
        for (var i = 0; i < activeCount; i++)
            status.ActiveDrives.Add(PipeResponseBinarySerializer.ReadString(payload, ref offset));

        var driveCount = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        offset += 4;
        if (driveCount < 0 || driveCount > (payload.Length - offset) / 17)
            throw new InvalidDataException("Invalid drive index status count.");
        for (var i = 0; i < driveCount; i++)
        {
            var drive = PipeResponseBinarySerializer.ReadString(payload, ref offset);
            var enabled = payload[offset++] != 0;
            var kind = PipeResponseBinarySerializer.ReadString(payload, ref offset);
            var state = PipeResponseBinarySerializer.ReadString(payload, ref offset);
            var files = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
            offset += 4;
            var dirs = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
            offset += 4;
            var cachePath = PipeResponseBinarySerializer.ReadString(payload, ref offset);

            status.Drives.Add(new UsnIndexer.DriveIndexStatus
            {
                Drive = drive,
                Enabled = enabled,
                Kind = kind,
                State = state,
                Files = files,
                Dirs = dirs,
                CachePath = cachePath,
            });
        }
        return status;
    }
}
