using System.Text;
using System.Text.Json;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

public sealed class LocalSendClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly LocalSendServer? _server;
    private LocalSendPendingFileTransfer? _pendingFileTransfer;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public LocalSendClient(LocalSendServer? server = null)
    {
        _server = server;
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            UseProxy = false
        };
        _httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<LocalSendDeviceInfo?> GetDeviceInfoAsync(string ip, int port = 53317, bool https = false, CancellationToken token = default, string? targetVersion = null, string? fingerprint = null)
    {
        try
        {
            var cleanIp = LocalSendServerHelper.CleanIpAddress(ip);
            var url = LocalSendApiRoute.BuildUri(cleanIp, port, https, "info", targetVersion).ToString();
            if (!string.IsNullOrEmpty(fingerprint))
                url += $"?fingerprint={Uri.EscapeDataString(fingerprint)}";
            var response = await _httpClient.GetAsync(url, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var info = JsonSerializer.Deserialize<LocalSendInfoDto>(json);
            return info == null ? null : LocalSendProtocolMapper.ToDevice(info, cleanIp, port, https ? "https" : "http");
        }
        catch { return null; }
    }

    public async Task<LocalSendSendResult> SendTextAsync(
        string targetIp, int targetPort, bool https, LocalSendDeviceInfo senderInfo, string text, string? pin = null,
        CancellationToken token = default, string? targetVersion = null)
    {
        var rawGuid = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var fileId = $"text_{rawGuid.Replace("-", string.Empty)}";
        var fileName = $"{rawGuid}.txt";

        var dto = new LocalSendPrepareUploadRequestDto
        {
            Info = LocalSendProtocolMapper.CreateInfoRegister(senderInfo),
            Files = new Dictionary<string, LocalSendFileDto>
            {
                [fileId] = new LocalSendFileDto
                {
                    Id = fileId,
                    FileName = fileName,
                    Size = Encoding.UTF8.GetByteCount(text),
                    FileType = LocalSendApiRoute.UsesV1(targetVersion) ? "text" : "text/plain",
                    Preview = text
                }
            }
        };

        var (prepResult, sessionId, tokens, usedHttps, prepErr) = await LocalSendClientHelper.PrepareUploadAsync(
            _httpClient, JsonOptions, targetIp, targetPort, https, dto, pin, token, targetVersion).ConfigureAwait(false);

        if (prepResult != LocalSendSendResult.Success)
        {
            LastError = prepErr;
            if (prepResult == LocalSendSendResult.Canceled)
                await CancelSessionAsync(targetIp, targetPort, usedHttps, sessionId ?? string.Empty, CancellationToken.None, targetVersion).ConfigureAwait(false);
            return prepResult;
        }

        return LocalSendSendResult.Success;
    }

    public async Task<LocalSendSendResult> SendFilesAsync(
        string targetIp, int targetPort, bool https, LocalSendDeviceInfo senderInfo, IReadOnlyList<string> filePaths,
        string? pin = null, Action<LocalSendSendProgressArgs>? onProgress = null,
        Action<LocalSendFileConfirmationArgs>? onFileConfirmed = null, CancellationToken token = default, string? targetVersion = null)
    {
        _pendingFileTransfer = null;
        if (filePaths.Count == 0) return LocalSendSendResult.Error;

        var expandedItems = new List<(string absolutePath, string relativePath)>();
        foreach (var p in filePaths)
        {
            if (File.Exists(p))
            {
                expandedItems.Add((p, Path.GetFileName(p)));
            }
            else if (Directory.Exists(p))
            {
                var fullPath = Path.GetFullPath(p);
                var parentDir = Path.GetDirectoryName(fullPath);
                var baseDir = string.IsNullOrEmpty(parentDir) ? fullPath : parentDir;

                try
                {
                    var files = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories);
                    foreach (var f in files)
                    {
                        var relPath = Path.GetRelativePath(baseDir, f).Replace('\\', '/');
                        expandedItems.Add((f, relPath));
                    }
                }
                catch { }
            }
        }

        if (expandedItems.Count == 0) return LocalSendSendResult.Error;

        var filesDict = new Dictionary<string, LocalSendFileDto>();
        var pathMap = new Dictionary<string, string>();
        for (var i = 0; i < expandedItems.Count; i++)
        {
            var (path, relPath) = expandedItems[i];
            var fi = new FileInfo(path);
            var id = $"file_{i}_{Guid.NewGuid():N}";
            filesDict[id] = new LocalSendFileDto
            {
                Id = id,
                FileName = relPath,
                Size = fi.Length,
                FileType = LocalSendClientHelper.GetFileType(fi.Extension, LocalSendApiRoute.UsesV1(targetVersion)),
                Metadata = new LocalSendFileMetadataDto
                {
                    LastModified = fi.LastWriteTimeUtc,
                    LastAccessed = fi.LastAccessTimeUtc
                }
            };
            pathMap[id] = path;
        }

        var prepareDto = new LocalSendPrepareUploadRequestDto { Info = LocalSendProtocolMapper.CreateInfoRegister(senderInfo), Files = filesDict };
        var (prepResult, sessionId, tokens, usedHttps, prepErr) = await LocalSendClientHelper.PrepareUploadAsync(_httpClient, JsonOptions, targetIp, targetPort, https, prepareDto, pin, token, targetVersion).ConfigureAwait(false);
        if (prepResult != LocalSendSendResult.Success || tokens == null || (!LocalSendApiRoute.UsesV1(targetVersion) && string.IsNullOrEmpty(sessionId)))
        {
            LastError = prepErr;
            if (prepResult == LocalSendSendResult.Canceled)
                await CancelSessionAsync(targetIp, targetPort, usedHttps, sessionId ?? string.Empty, CancellationToken.None, targetVersion).ConfigureAwait(false);
            return prepResult;
        }

        var cleanIp = LocalSendServerHelper.CleanIpAddress(targetIp);
        _pendingFileTransfer = new LocalSendPendingFileTransfer
        {
            TargetIp = cleanIp,
            TargetPort = targetPort,
            Https = usedHttps,
            SessionId = sessionId,
            TargetVersion = targetVersion,
            Tokens = tokens,
            Files = filesDict.Select(pair => new LocalSendPendingFile(pair.Key, pair.Value, pathMap[pair.Key])).ToArray()
        };
        return await UploadPendingFilesAsync(onProgress, onFileConfirmed, token).ConfigureAwait(false);
    }

    public bool HasRetryableFileSend => _pendingFileTransfer?.HasFailedFiles == true;

    public Task<LocalSendSendResult> RetryLastFailedFileAsync(Action<LocalSendSendProgressArgs>? onProgress = null,
        Action<LocalSendFileConfirmationArgs>? onFileConfirmed = null, CancellationToken token = default) =>
        _pendingFileTransfer == null ? Task.FromResult(LocalSendSendResult.Error) : UploadPendingFilesAsync(onProgress, onFileConfirmed, token);

    private async Task<LocalSendSendResult> UploadPendingFilesAsync(Action<LocalSendSendProgressArgs>? onProgress,
        Action<LocalSendFileConfirmationArgs>? onFileConfirmed, CancellationToken token)
    {
        var transfer = _pendingFileTransfer!;
        var attempt = await LocalSendFileTransferSender.UploadAsync(_httpClient, _server, transfer, onProgress, onFileConfirmed, token).ConfigureAwait(false);
        LastError = attempt.Error;
        if (attempt.Result == LocalSendSendResult.Canceled)
            await CancelSessionAsync(transfer.TargetIp, transfer.TargetPort, transfer.Https, transfer.SessionId ?? string.Empty, CancellationToken.None, transfer.TargetVersion).ConfigureAwait(false);
        if (!attempt.CanRetry)
            _pendingFileTransfer = null;
        return attempt.Result;
    }

    public async Task CancelSessionAsync(string targetIp, int targetPort, bool https, string sessionId, CancellationToken token = default, string? targetVersion = null)
    {
        try
        {
            var cleanIp = LocalSendServerHelper.CleanIpAddress(targetIp);
            var url = LocalSendApiRoute.BuildUri(cleanIp, targetPort, https, "cancel", targetVersion).ToString();
            if (!LocalSendApiRoute.UsesV1(targetVersion))
                url += $"?sessionId={Uri.EscapeDataString(sessionId)}";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await _httpClient.PostAsync(url, content: null, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Log($"[LocalSendClient] Failed to send /cancel POST: {ex.Message}", LogLevel.Warn);
        }
    }

    public string? LastError { get; private set; }

    public void Dispose() => _httpClient.Dispose();
}
