using System.Text.Json.Serialization;

namespace Lertaro.Core.Services.LocalSend.Models;

/// <summary>
/// Device information exchanged during LocalSend v2 discovery and registration.
/// </summary>
public sealed class LocalSendDeviceInfo
{
    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "2.2";

    [JsonPropertyName("deviceModel")]
    public string? DeviceModel { get; set; } = "Windows";

    [JsonPropertyName("deviceType")]
    public string? DeviceType { get; set; } = "desktop";

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("port")]
    public int Port { get; set; } = 53317;

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = "http";

    [JsonPropertyName("download")]
    public bool Download { get; set; } = false;

    [JsonPropertyName("announcement")]
    public bool? Announcement { get; set; } = true;

    [JsonPropertyName("announce")]
    public bool? Announce { get; set; } = true;

    [JsonPropertyName("isBusy")]
    public bool? IsBusy { get; set; }

    [JsonPropertyName("busy")]
    public bool? BusyFallback { set { if (value.HasValue) IsBusy = value; } }

    [JsonIgnore]
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// The identity a device keeps when it moves. <see cref="Fingerprint"/> is the hash of the peer's
    /// self-signed certificate, so it survives the DHCP lease that does not: one device answering on a new
    /// address re-registers under this same key and the newest address simply replaces the old, which is how
    /// upstream LocalSend keys its device map.
    ///
    /// The port stays in the key because one machine is not one target. Two Windows users logged in at once
    /// run two Lertaro instances -- the single-instance mutex is Local\, so it is scoped to a session -- and
    /// each holds its own certificate under its own per-user data directory, with its own port once the
    /// first took 53317. That already separates them twice over; the port is the half that keeps working
    /// when the peer is not Lertaro, because LocalSend itself persists one certificate per user profile, so
    /// two of its instances on one machine share a fingerprint and differ only by port. Key on the
    /// fingerprint alone and a send goes to whichever session announced last, while the dialog the transfer
    /// is waiting on belongs to the other user's desktop.
    ///
    /// The IP stays in only when the peer answered with no fingerprint at all, which leaves nothing else to
    /// tell two rows apart.
    /// </summary>
    [JsonIgnore]
    public string DiscoveryKey => string.IsNullOrEmpty(Fingerprint)
        ? $"{IpAddress}\u001F{Port}"
        : $"{Port}\u001F{Fingerprint}";

    [JsonIgnore]
    public bool Https => string.Equals(Protocol, "https", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// File metadata entry in a prepare-upload request.
/// </summary>
public sealed class LocalSendFileDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("fileType")]
    public string FileType { get; set; } = "other";

    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("preview")]
    public string? Preview { get; set; }

    [JsonPropertyName("metadata")]
    public LocalSendFileMetadataDto? Metadata { get; set; }
}

public sealed class LocalSendFileMetadataDto
{
    [JsonPropertyName("modified")] public DateTime? LastModified { get; set; }
    [JsonPropertyName("accessed")] public DateTime? LastAccessed { get; set; }
}

/// <summary>
/// Request payload for POST /api/localsend/v2/prepare-upload.
/// </summary>
public sealed class PrepareUploadRequestDto
{
    [JsonPropertyName("info")]
    public LocalSendDeviceInfo Info { get; set; } = new();

    [JsonPropertyName("files")]
    public Dictionary<string, LocalSendFileDto> Files { get; set; } = new();
}

/// <summary>
/// Response payload for POST /api/localsend/v2/prepare-upload.
/// </summary>
public sealed class PrepareUploadResponseDto
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("files")]
    public Dictionary<string, string> Files { get; set; } = new();
}

public sealed class LocalSendUploadRequestArgs : EventArgs
{
    public string SessionId { get; }
    public PrepareUploadRequestDto Dto { get; }
    public string? CustomDownloadDirectory { get; set; }
    public HashSet<string>? SelectedFileIds { get; set; }
    public bool IsAutoAccepted { get; }
    private readonly Action<bool> _respond;

    public LocalSendUploadRequestArgs(string sessionId, PrepareUploadRequestDto dto, Action<bool> respond, bool isAutoAccepted = false)
    {
        SessionId = sessionId;
        Dto = dto;
        _respond = respond;
        IsAutoAccepted = isAutoAccepted;
    }

    public void Respond(bool accept) => _respond(accept);
}

public enum LocalSendSendResult
{
    Success,
    Declined,
    Busy,
    InvalidPin,
    TooManyAttempts,
    Canceled,
    ReceiverCanceled,
    RemoteError,
    Error
}

public enum LocalSendTransferStage
{
    Transferring,
    CalculatingChecksum,
    WaitingForConfirmation,
    VerifyingChecksum
}

public sealed class LocalSendSendProgressArgs
{
    public string FileName { get; }
    public long BytesSent { get; }
    public long TotalBytes { get; }
    public int FileIndex { get; }
    public int TotalFiles { get; }
    public LocalSendTransferStage Stage { get; }

    public LocalSendSendProgressArgs(string fileName, long bytesSent, long totalBytes, int fileIndex, int totalFiles,
        LocalSendTransferStage stage = LocalSendTransferStage.Transferring)
    {
        FileName = fileName;
        BytesSent = bytesSent;
        TotalBytes = totalBytes;
        FileIndex = fileIndex;
        TotalFiles = totalFiles;
        Stage = stage;
    }
}
