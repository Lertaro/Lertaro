using System.Runtime.CompilerServices;
using System.Windows;
using Lertaro.App.Services;
using Lertaro.Core.Wire;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// Refreshes the empty inline list from Hook snapshots; callbacks may arrive off the UI thread. Request
// once for each docked host's first visible pass. Key by host HWND, since the cached card is reused
// across summons. Repeated visibility requests can trigger slow external-tool queries and block
// interaction.
internal static class InlineOpenedFoldersRefreshHelper
{
    // Per card window instance, the host its last sent request was made for. The value is boxed because the
    // table only holds reference types, and is only ever read back through `is IntPtr`.
    private static readonly ConditionalWeakTable<Window, object> RequestedForHost = [];

    public static void Attach(Lertaro.App.InlineSearchWindow window)
    {
        var hookClient = App.HookClient;

        void RequestSnapshotForHost()
        {
            // Unsent means unrecorded too: a card first shown while the Hook pipe was down used to be marked
            // as asked for and then never asked again for the rest of its life.
            if (hookClient?.IsConnected != true)
                return;

            var host = InlineSearchManager.Instance.CurrentHostHwnd;
            if (RequestedForHost.TryGetValue(window, out var askedFor) && askedFor is IntPtr previous && previous == host)
                return;

            RequestedForHost.AddOrUpdate(window, host);
            hookClient.SendMessage(new IpcMessage { Id = IpcMessageId.RequestOpenedFolders });
        }

        void RefreshEmptyState()
        {
            if (window.IsVisible)
                window.ViewModel.Search.RefreshEmptyState();
        }

        void OnVisibleChanged(object? sender, DependencyPropertyChangedEventArgs args)
        {
            if (!window.IsVisible)
                return;

            RefreshEmptyState();
            RequestSnapshotForHost();
        }

        void OnSnapshotCaptured(IReadOnlyList<string> _)
        {
            // Queue behind PluginSdkBridge.UpdateOpenedFolders, which is subscribed to the same event
            // and updates the store that ExplorerPathService reads.
            if (!window.Dispatcher.HasShutdownStarted)
                window.Dispatcher.BeginInvoke(new Action(RefreshEmptyState));
        }

        void OnClosed(object? sender, EventArgs args)
        {
            window.IsVisibleChanged -= OnVisibleChanged;
            window.Closed -= OnClosed;
            hookClient?.OnOpenedFoldersCaptured -= OnSnapshotCaptured;
            RequestedForHost.Remove(window);
        }

        window.IsVisibleChanged += OnVisibleChanged;
        window.Closed += OnClosed;
        hookClient?.OnOpenedFoldersCaptured += OnSnapshotCaptured;
    }
}
