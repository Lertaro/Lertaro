using System.Text.Json.Serialization;

namespace Lertaro.Core.Services.LocalSend.Models;

/// <summary>Wire format returned by the LocalSend info endpoint.</summary>
public sealed class LocalSendInfoDto
{
    [JsonPropertyName("alias")] public string Alias { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("deviceModel")] public string? DeviceModel { get; set; }
    [JsonPropertyName("deviceType")] public string? DeviceType { get; set; }
    [JsonPropertyName("fingerprint")] public string? Fingerprint { get; set; }
    [JsonPropertyName("download")] public bool? Download { get; set; }
}

/// <summary>Wire format sent to the LocalSend register endpoint.</summary>
public sealed class LocalSendRegisterDto
{
    [JsonPropertyName("alias")] public string Alias { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("deviceModel")] public string? DeviceModel { get; set; }
    [JsonPropertyName("deviceType")] public string? DeviceType { get; set; }
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = string.Empty;
    [JsonPropertyName("port")] public int? Port { get; set; }
    [JsonPropertyName("protocol")] public string? Protocol { get; set; }
    [JsonPropertyName("download")] public bool? Download { get; set; }
}

/// <summary>Wire format transported through LocalSend multicast discovery.</summary>
public sealed class LocalSendMulticastDto
{
    [JsonPropertyName("alias")] public string Alias { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("deviceModel")] public string? DeviceModel { get; set; }
    [JsonPropertyName("deviceType")] public string? DeviceType { get; set; }
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = string.Empty;
    [JsonPropertyName("port")] public int? Port { get; set; }
    [JsonPropertyName("protocol")] public string? Protocol { get; set; }
    [JsonPropertyName("download")] public bool? Download { get; set; }
    [JsonPropertyName("announcement"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Announcement { get; set; }
    [JsonPropertyName("announce"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Announce { get; set; }
}

/// <summary>Sender identity embedded in a prepare-upload request.</summary>
public sealed class LocalSendInfoRegisterDto
{
    [JsonPropertyName("alias")] public string Alias { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("deviceModel")] public string? DeviceModel { get; set; }
    [JsonPropertyName("deviceType")] public string? DeviceType { get; set; }
    [JsonPropertyName("fingerprint")] public string? Fingerprint { get; set; }
    [JsonPropertyName("port")] public int? Port { get; set; }
    [JsonPropertyName("protocol")] public string? Protocol { get; set; }
    [JsonPropertyName("download")] public bool? Download { get; set; }
}

/// <summary>Wire format accepted by the LocalSend prepare-upload endpoint.</summary>
public sealed class LocalSendPrepareUploadRequestDto
{
    [JsonPropertyName("info")] public LocalSendInfoRegisterDto Info { get; set; } = new();
    [JsonPropertyName("files")] public Dictionary<string, LocalSendFileDto> Files { get; set; } = new();
}
