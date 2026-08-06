using System.IO.Pipes;

using Lertaro.Core.Wire;

namespace Lertaro.Core.Services.Search;

/// <summary>
/// Subscribes to the service for changes under a given set of directories, and nothing else.
/// </summary>
/// <remarks>
/// Its own connection, not a field on the status subscription: a connection parked in a streaming loop
/// cannot accept another request, so the two subscriptions cannot share one.
///
/// The watch list goes out once, with the subscribe. Changing it means resubscribing -- which is what
/// the caller does when a plugin registers or unregisters a directory, and is rare compared to the rate
/// changes arrive at on the other side.
/// </remarks>
public static class DirectoryChangeStream
{
    public static async Task SubscribeAsync(IReadOnlyList<string> watched, Action<IReadOnlyList<string>> onChanged, CancellationToken token)
    {
        using var pipe = new NamedPipeClientStream(".", "LertaroPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(2000, token).ConfigureAwait(false);
        await SearchRequestBinarySerializer.WriteSearchRequestAsync(pipe, new SearchRequestMessage
        {
            Id = SearchRequestId.SubscribeDirectoryChanges,
            Directories = watched.ToList()
        }, token).ConfigureAwait(false);

        while (!token.IsCancellationRequested && pipe.IsConnected)
        {
            var response = await PipeResponseBinarySerializer.ReadAsync(pipe, token).ConfigureAwait(false);
            if (response.Kind != PipeResponseKind.DirectoriesChanged || response.ChangedDirectories == null)
                break;

            onChanged(response.ChangedDirectories);
        }
    }
}
