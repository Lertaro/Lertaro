using System.Windows;
using Lertaro.Core.Wire;

namespace Lertaro.App.Views.InlineSearchWindow.Helpers;

// Keeps the empty inline-search list synchronized with the Hook snapshot without making the Hook
// request part of the search dispatcher. The snapshot callback may arrive off the UI thread.
internal static class InlineOpenedFoldersRefreshHelper
{
    public static void Attach(Lertaro.App.InlineSearchWindow window)
    {
        var hookClient = App.HookClient;

        void RequestSnapshot()
        {
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
            RequestSnapshot();
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
        }

        window.IsVisibleChanged += OnVisibleChanged;
        window.Closed += OnClosed;
        hookClient?.OnOpenedFoldersCaptured += OnSnapshotCaptured;
    }
}
