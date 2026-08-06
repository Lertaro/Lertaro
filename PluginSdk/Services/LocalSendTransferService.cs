namespace Lertaro.PluginSdk.Services;

/// <summary>
/// A decoupled service allowing plugins to trigger LocalSend send sessions.
/// </summary>
public static class LocalSendTransferService
{
    public static Action<IReadOnlyList<string>?, string?>? OpenSendWindowFunc { get; set; }

    public static void OpenSendWindow(IReadOnlyList<string>? files, string? text) => OpenSendWindowFunc?.Invoke(files, text);
}
