namespace Lertaro.Core.Indexer.NetworkDrive;

public sealed class NetworkIndexStatus
{
    public string Drive { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int Items { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public int EnumerateErrors { get; set; }
    public int AttributeErrors { get; set; }
    public int ReparseSkipped { get; set; }
    public int SlowDirectories { get; set; }
    public string CachePath { get; set; } = string.Empty;
    public DateTime? LastUpdated { get; set; }
    public string Error { get; set; } = string.Empty;
    public NetworkIndexStatus Clone() => new()
    {
        Drive = Drive,
        State = State,
        Items = Items,
        Skipped = Skipped,
        Errors = Errors,
        EnumerateErrors = EnumerateErrors,
        AttributeErrors = AttributeErrors,
        ReparseSkipped = ReparseSkipped,
        SlowDirectories = SlowDirectories,
        CachePath = CachePath,
        LastUpdated = LastUpdated,
        Error = Error
    };
}
