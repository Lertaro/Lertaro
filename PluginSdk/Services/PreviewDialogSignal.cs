namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Raised by a preview provider when a native handler's own popup dialog appears (e.g. Word's "Enter
/// password" prompt for an encrypted file it's asked to preview -- see PreviewActivationSignal's own
/// comment for the broader out-of-process-handler context) and again once that dialog closes. The app
/// subscribes once to hide the quick window and its preview window for as long as the dialog is up --
/// left alone, both windows keep floating on top of a dialog that's otherwise unreachable behind them --
/// and restore both the moment it goes away.
/// </summary>
public static class PreviewDialogSignal
{
    public static event Action? DialogOpened;
    public static event Action? DialogClosed;

    public static void NotifyDialogOpened() => DialogOpened?.Invoke();
    public static void NotifyDialogClosed() => DialogClosed?.Invoke();
}
