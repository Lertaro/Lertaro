using System.Runtime.CompilerServices;
using System.Windows;
using Lertaro.Core.Wire;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// Keeps the empty inline-search list synchronized with the Hook snapshot without making the Hook
// request part of the search dispatcher. The snapshot callback may arrive off the UI thread.
//
// The snapshot is requested ONCE per window instance, when that window first becomes visible. Asking on
// every IsVisibleChanged looked harmless but was not: the Hook answers a request by querying Directory
// Opus through a separate process (up to a multi-second wait on the hook thread) and re-scraping the
// file-display windows, and the window is made visible repeatedly while the user works -- measured at
// one request every ~5s, each of which froze the UI long enough that the user could not click. The
// opened folders cannot meaningfully change within one showing anyway.
internal static class InlineOpenedFoldersRefreshHelper
{
    // The window instances whose snapshot has already been requested. Weak enough not to keep a closed
    // window alive, and keyed by instance so a NEW window still loads its own snapshot.
    private static readonly ConditionalWeakTable<Window, object> SnapshotRequested = [];

    public static void Attach(Lertaro.App.InlineSearchWindow window)
    {
        var hookClient = App.HookClient;

        void RequestSnapshotOnce()
        {
            // Already recorded means this window has had its one request, so it must not ask again.
            if (SnapshotRequested.TryGetValue(window, out _))
                return;

            SnapshotRequested.Add(window, new object());

            if (hookClient?.IsConnected == true)
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
            RequestSnapshotOnce();
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
            SnapshotRequested.Remove(window);
        }

        window.IsVisibleChanged += OnVisibleChanged;
        window.Closed += OnClosed;
        hookClient?.OnOpenedFoldersCaptured += OnSnapshotCaptured;
    }
}
