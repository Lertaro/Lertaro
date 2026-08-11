namespace Lertaro.Core.IndexV2.Space;

public readonly record struct IndexedSpaceEntry(
    int Row,
    string Name,
    long Size,
    bool IsDirectory,
    bool IsHardLinkDuplicate);
