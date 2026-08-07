using System.IO.Pipes;
using Lertaro.Core;
using Lertaro.App.Services.UrlProtocol;
using Lertaro.Core.Wire;
namespace Lertaro.App.Services.Pipe;

public static class AppPipeService
{
    private static bool _keepRunningPipeServer = true;

    public static void StopServer() => _keepRunningPipeServer = false;

    // uri, when given, is forwarded as-is instead of the plain "ACTIVATE" command -- the running
    // instance's server loop below treats anything other than "ACTIVATE" as a lertaro:// URI to route.
    public static async Task SendActivateSignalAsync(string? uri = null, CancellationToken token = default)
    {
        var pipeName = AppPipeNames.ActivationPipeName;

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);

            await client.ConnectAsync(500, token).ConfigureAwait(false);
            await PipeRequestBinarySerializer.WriteStringAsync(client, uri ?? "ACTIVATE", token).ConfigureAwait(false);
        }

        catch (Exception ex)
        {
            Logger.Log($"Failed to send activation signal: {ex.Message}", LogLevel.Error);
        }
    }

    public static Task StartPipeServerAsync() => RunPipeServerAsync();

    private static async Task RunPipeServerAsync()
    {
        var pipeName = AppPipeNames.ActivationPipeName;
        while (_keepRunningPipeServer)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync();
                var msg = await PipeRequestBinarySerializer.ReadStringAsync(server);
                if (msg == "ACTIVATE")
                {
                    _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (System.Windows.Application.Current.MainWindow is QuickSearchWindow quickSearchWindow)
                        {
                            quickSearchWindow.ShowWindow();
                        }

                    }));
                }
                else
                {
                    UriRouter.Route(msg);
                }
            }

            catch (Exception ex)
            {
                Logger.Log($"[AppPipeService] Named pipe server error: {ex.Message}", LogLevel.Error);

                await Task.Delay(1000); // Prevent tight loop on error
            }
        }
    }
}
