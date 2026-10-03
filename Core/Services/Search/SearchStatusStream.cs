using Lertaro.Core.Indexer.Usn;

using Lertaro.Core.Wire;
namespace Lertaro.Core.Services.Search;

public static class SearchStatusStream
{
    public static async Task SubscribeAsync(Action<UsnIndexer.IndexerStatus> onStatus, CancellationToken token)
    {
        using var pipe = await ServicePipe.ConnectAsync(2000, token).ConfigureAwait(false);
        await SearchRequestBinarySerializer.WriteSearchRequestAsync(pipe, new SearchRequestMessage
        {
            Id = SearchRequestId.SubscribeStatus
        }, token).ConfigureAwait(false);

        while (!token.IsCancellationRequested && pipe.IsConnected)
        {
            var response = await PipeResponseBinarySerializer.ReadAsync(pipe, token).ConfigureAwait(false);
            if (response.Kind != PipeResponseKind.Status || response.Status == null)
                break;

            onStatus(response.Status);
        }
    }
}
