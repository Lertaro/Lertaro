namespace Lertaro.Core;

// A file's Size + Creation/LastWrite/LastAccess timestamps (whole-second Unix time, UTC), as stored
// in the index -- the wire-format DTO for the GetFileMetadata pipe request/response.
public readonly record struct FileMetadataEntry(long Size, uint CreationTimeUnixSeconds, uint LastWriteTimeUnixSeconds, uint LastAccessTimeUnixSeconds);
