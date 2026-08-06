using Lertaro.App.Services;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.App.ViewModels.LocalSend;

/// <summary>Formats LocalSend send status text outside the view model to keep it focused on state.</summary>
internal static class LocalSendSendPresentation
{
    internal static string GetStatus(LocalSendSendResult result, string? errorDetails, string prefix) => result switch
    {
        LocalSendSendResult.Success => prefix + TranslationManager.Instance["Settings_LocalSend_Completed"],
        LocalSendSendResult.Declined => prefix + TranslationManager.Instance["Settings_LocalSend_Declined"],
        LocalSendSendResult.Busy => prefix + TranslationManager.Instance["Settings_LocalSend_Busy"],
        LocalSendSendResult.InvalidPin => prefix + TranslationManager.Instance["Settings_LocalSend_InvalidPin"],
        LocalSendSendResult.Canceled => prefix + TranslationManager.Instance["Settings_LocalSend_Canceled"],
        LocalSendSendResult.ReceiverCanceled => prefix + TranslationManager.Instance["Settings_LocalSend_ReceiverCanceled"],
        LocalSendSendResult.RemoteError => prefix + TranslationManager.Instance["Settings_LocalSend_RemoteError"],
        _ => prefix + $"{TranslationManager.Instance["Settings_LocalSend_ConnectionError"]} ({errorDetails ?? result.ToString()})"
    };

    internal static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{(double)bytes / 1024:F1} KB",
        < 1024 * 1024 * 1024 => $"{(double)bytes / (1024.0 * 1024.0):F1} MB",
        _ => $"{(double)bytes / (1024.0 * 1024.0 * 1024.0):F1} GB"
    };
}
