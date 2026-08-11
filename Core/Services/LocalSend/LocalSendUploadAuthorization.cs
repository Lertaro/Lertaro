namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Validates the sender and one-time file tokens issued for a LocalSend upload session.
/// </summary>
internal sealed class LocalSendUploadAuthorization
{
    private readonly string _senderIp;
    private readonly IReadOnlyDictionary<string, string> _fileTokens;
    private readonly Dictionary<string, UploadState> _uploadStates;
    private readonly object _stateLock = new();

    public LocalSendUploadAuthorization(string senderIp, IReadOnlyDictionary<string, string> fileTokens)
    {
        _senderIp = LocalSendServerHelper.CleanIpAddress(senderIp);
        _fileTokens = fileTokens;
        _uploadStates = fileTokens.Keys.ToDictionary(fileId => fileId, _ => new UploadState());
    }

    public bool Allows(string senderIp, string fileId, string token) =>
        MatchesSender(senderIp) && AllowsToken(fileId, token);

    internal bool MatchesSender(string senderIp) =>
        string.Equals(_senderIp, LocalSendServerHelper.CleanIpAddress(senderIp), StringComparison.OrdinalIgnoreCase);

    internal bool AllowsToken(string fileId, string token) =>
        _fileTokens.TryGetValue(fileId, out var expectedToken) && string.Equals(expectedToken, token, StringComparison.Ordinal);

    internal bool TryBeginUpload(string fileId)
    {
        lock (_stateLock)
        {
            if (!_uploadStates.TryGetValue(fileId, out var state) || state.Status != UploadStatus.Pending || state.Attempts >= 3)
                return false;
            state.Attempts++;
            state.Status = UploadStatus.InProgress;
            return true;
        }
    }

    internal bool CompleteUpload(string fileId, LocalSendFileSaveStatus result)
    {
        lock (_stateLock)
        {
            if (!_uploadStates.TryGetValue(fileId, out var state) || state.Status != UploadStatus.InProgress)
                return false;
            state.Status = result switch
            {
                LocalSendFileSaveStatus.Success => UploadStatus.Finished,
                LocalSendFileSaveStatus.ChecksumMismatch when state.Attempts < 3 => UploadStatus.Pending,
                _ => UploadStatus.Failed
            };
            return _uploadStates.Values.All(item => item.Status is UploadStatus.Finished or UploadStatus.Failed);
        }
    }

    private sealed class UploadState
    {
        internal int Attempts { get; set; }
        internal UploadStatus Status { get; set; }
    }

    private enum UploadStatus { Pending, InProgress, Finished, Failed }
}
