namespace Lertaro.Core.IndexV2.Space;

/// <summary>A detached, wire-safe view of one entry in the current in-memory index.</summary>
public readonly record struct SpaceIndexEntry(
    string Path,
    string Name,
    long Size,
    bool IsDirectory,
    bool IsHardLinkDuplicate);
