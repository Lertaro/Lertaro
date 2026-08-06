namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Validates the sender and one-time file tokens issued for a LocalSend upload session.
/// </summary>
internal sealed class LocalSendUploadAuthorization
{
    private readonly string _senderIp;
    private readonly IReadOnlyDictionary<string, string> _fileTokens;

    public LocalSendUploadAuthorization(string senderIp, IReadOnlyDictionary<string, string> fileTokens)
    {
        _senderIp = LocalSendServerHelper.CleanIpAddress(senderIp);
        _fileTokens = fileTokens;
    }

    public bool Allows(string senderIp, string fileId, string token) =>
        MatchesSender(senderIp) && AllowsToken(fileId, token);

    internal bool MatchesSender(string senderIp) =>
        string.Equals(_senderIp, LocalSendServerHelper.CleanIpAddress(senderIp), StringComparison.OrdinalIgnoreCase);

    internal bool AllowsToken(string fileId, string token) =>
        _fileTokens.TryGetValue(fileId, out var expectedToken) && string.Equals(expectedToken, token, StringComparison.Ordinal);
}
