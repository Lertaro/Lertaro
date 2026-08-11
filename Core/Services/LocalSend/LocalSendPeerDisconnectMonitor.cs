namespace Lertaro.Core.Services.LocalSend;

/// <summary>Detects when a sender abandons a request that is waiting for receiver confirmation.</summary>
internal static class LocalSendPeerDisconnectMonitor
{
    internal static async Task<bool> WaitAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[1];
        try
        {
            while (await stream.ReadAsync(buffer, token).ConfigureAwait(false) > 0) { }
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }
}
