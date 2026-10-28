using System.Runtime.CompilerServices;
using System.Windows;
using Lertaro.App.Services;
using Lertaro.Core.Wire;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// Keeps the empty inline-search list synchronized with the Hook snapshot without making the Hook request
// part of the search dispatcher. The snapshot callback may arrive off the UI thread.
//
// The snapshot is requested once per host the card is docked to, on that card's first visible pass over it.
// Asking on every IsVisibleChanged looked harmless but was not: the Hook answers a request by querying
// Directory Opus through a separate process (up to a multi-second wait on the hook thread) and re-scraping
// the file-display windows, and the window is made visible repeatedly while the user works -- measured at
// one request every ~5s, each of which froze the UI long enough that the user could not click. The opened
// folders cannot meaningfully change within one showing, and one request per showing is what that cost
// buys back.
//
// This used to key on the window instance, which stopped meaning "one showing" when the card became
// reusable across summons (see InlineSearchWindowSummonSupport.Summon): one instance then served every
// dialog for the whole run and asked the Hook once, so the list held whatever file managers happened to be
// open at the first summon and never caught up with the rest. The host handle is the thing a summon
// actually changes -- it is assigned before the window is shown -- so the request keys on that now.
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
