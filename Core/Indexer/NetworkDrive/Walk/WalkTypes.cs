namespace Lertaro.Core.Indexer.NetworkDrive.Walk;

internal readonly record struct NetworkWalkRecord(FileRecord Record, FileAttributes Attributes)
{
    public UInt128 Id => Record.Id;
    public UInt128 ParentId => Record.ParentId;
    public string Name => Record.Name;
    public FileRecordFlags Flags => Record.Flags;
    public FileAttributes Attributes { get; } = Attributes;

    public static implicit operator FileRecord(NetworkWalkRecord record) => record.Record;
}

internal enum WalkRecordResult
{
    Success,
    AttributeError,
    ReparsePoint,
    InvalidName
}

internal readonly record struct WorkItem(string Path, string LogicalPath, UInt128 LocalId, int Depth, NetworkIgnoreRuleSet IgnoreRules, AncestorNode? Ancestors = null);
