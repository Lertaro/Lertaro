using Lertaro.Core.Indexer.Usn;
using Application = System.Windows.Application;

using Lertaro.Core.Services.Search;

namespace Lertaro.App.Services;

internal static class SearchIndexBuildCoordinator
{
    public static void Trigger(
        SearchService searchService,
        ServiceConnectionHandler connectionHandler,
        Func<bool> shouldWaitForReconnect,
        Action resetAutoInstallFlag,
        Action<UsnIndexer.IndexerStatus> onReadyStatus,
        Action<UsnIndexer.IndexerStatus> onPendingStatus,
        bool forceRebuild = false) => _ = Task.Run(async () =>
                                           {
                                               var status = await SearchIndexBootstrapHelper.EnsureInitializedAsync(searchService, forceRebuild).ConfigureAwait(false);

                                               _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                                               {
                                                   if (status.State == "ready")
                                                   {
                                                       connectionHandler.ClearServiceReconnectState();
                                                       onReadyStatus(status);
                                                       return;
                                                   }

                                                   if (!shouldWaitForReconnect())
                                                   {
                                                       resetAutoInstallFlag();
                                                   }

                                                   onPendingStatus(status);
                                                   connectionHandler.Start(requireDetailedStatus: true);
                                               }));
                                           });
}
