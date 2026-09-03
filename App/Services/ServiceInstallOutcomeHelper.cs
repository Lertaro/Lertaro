namespace Lertaro.App.Services;

/// <summary>
/// Keeps the silent-install success rule testable without invoking the Windows service manager.
/// </summary>
internal static class ServiceInstallOutcomeHelper
{
    internal static ServiceInstallManager.SilentInstallResult DetermineResult(
        bool installerSucceeded,
        bool registeredAtCurrentPath,
        bool serviceStarted) =>
        installerSucceeded && registeredAtCurrentPath && serviceStarted
            ? ServiceInstallManager.SilentInstallResult.Started
            : ServiceInstallManager.SilentInstallResult.Failed;
}
