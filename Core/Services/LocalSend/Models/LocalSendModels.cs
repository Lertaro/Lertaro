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

    [JsonIgnore]
    public string DiscoveryKey => $"{IpAddress}\u001F{Port}\u001F{Fingerprint}";

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
