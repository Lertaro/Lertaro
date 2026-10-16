using System.IO;
using System.Text.Json;
using Lertaro.App.Services.AppLifecycle;
using Lertaro.App.Services.Plugin;
using Lertaro.Core;

namespace Lertaro.App.Services;

internal static class SettingsTransferRestart
{
    internal const string ArgumentPrefix = "--lertaro-settings-transfer=";
    internal sealed record Request(bool Export, string? Destination, bool IncludePluginFiles, string SuccessMessage);

    internal static string CreateRequestDirectory()
    {
        // UserDataAccess has already checked the current user's private data root. No SID/hash is rebuilt.
        using var root = new SettingsBackupPaths(Logger.UserDataDir);
        var relative = "ConfigTransfers/" + Guid.NewGuid().ToString("N");
        root.CreateParents(relative + "/payload/manifest.json");
        return root.Resolve(relative);
    }

    internal static async Task RestartAsync(string directory, Request request)
    {
        foreach (var plugin in PluginManager.Instance.Plugins)
            await plugin.PrepareForSettingsTransferAsync();
        using (var requestFolder = new SettingsBackupPaths(directory))
            requestFolder.Write("request.json", output => JsonSerializer.Serialize(output, request));
        if (!AppRestartService.RequestRestart(ArgumentPrefix + Path.GetFileName(directory)))
            throw new IOException("Could not restart Lertaro. The transfer has not been applied; restart the app before retrying.");
    }

    // Called before UserSettings.Load and plugin initialization. Keeping the runtime lease until
    // process exit prevents another session of this user from restoring beneath running plugins.
    internal static FileStream StartSession(IReadOnlyList<string> arguments, out string? message)
        => StartSession(Logger.UserDataDir, arguments, out message, AppContext.BaseDirectory, migrate: true);

    internal static FileStream StartSession(string dataDirectory, IReadOnlyList<string> arguments,
        out string? message, string? applicationDirectory = null, bool migrate = false)
    {
        try
        {
            return StartSessionCore(dataDirectory, arguments, out message, applicationDirectory, migrate);
        }
        catch (SettingsLeaseConflictException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Anything else that stops this session from opening is not a lease conflict, and telling the
            // user to close other instances for it (as the caller used to, unconditionally) is a diagnosis
            // that costs them the one clue this line keeps.
            Logger.Log($"[SettingsTransfer] Could not open the settings session for '{dataDirectory}': {ex}", LogLevel.Error);
            throw new SettingsSessionUnavailableException(dataDirectory, ex);
        }
    }

    private static FileStream StartSessionCore(string dataDirectory, IReadOnlyList<string> arguments,
        out string? message, string? applicationDirectory, bool migrate)
    {
        message = null;
        var requestId = ParseRequestId(arguments);
        FileStream exclusive;
        try { exclusive = SettingsBackup.OpenSession(dataDirectory, exclusive: true); }
        catch (IOException ex)
        {
            if (requestId != null) message = TransferFailure(ex);
            try { return SettingsBackup.OpenSession(dataDirectory, exclusive: false); }
            catch (IOException sharing) when (IsSharingViolation(sharing))
            {
                // Both the exclusive and the shared session were refused: that is another instance holding
                // the lease for a transfer, and the only case where saying so is true.
                throw new SettingsLeaseConflictException(dataDirectory, sharing);
            }
        }
        using (exclusive)
        {
            // Resolve the existing migration path before snapshotting, without loading settings objects.
            if (migrate) _ = UserSettings.SettingsPath;
            SettingsBackup.RecoverInterruptedImports(dataDirectory);
            if (requestId != null)
            {
                try
                {
                    var directory = Path.Combine(dataDirectory, "ConfigTransfers", requestId);
                    using var requestFolder = new SettingsBackupPaths(directory);
                    Request request;
                    using (var input = requestFolder.Read("request.json"))
                        request = JsonSerializer.Deserialize<Request>(input) ?? throw new InvalidDataException("Invalid settings transfer request.");
                    if (request.Export)
                    {
                        SettingsBackup.Export(dataDirectory, request.Destination ?? throw new InvalidDataException("Missing export destination."),
                            typeof(App).Assembly.GetName().Version?.ToString() ?? "", request.IncludePluginFiles, applicationDirectory);
                        message = string.Format(request.SuccessMessage, request.Destination);
                    }
                    else
                    {
                        var backup = SettingsBackup.Apply(Path.Combine(directory, "payload"), dataDirectory, request.IncludePluginFiles);
                        message = string.Format(request.SuccessMessage, backup);
                    }
                    // Consume the request only after a successful operation; failures keep staging/recovery data.
                    try
                    {
                        File.Delete(requestFolder.Resolve("request.json"));
                        requestFolder.ClearFiles("payload");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Logger.Log($"[SettingsTransfer] Completed, but could not clean staging: {ex.Message}", LogLevel.Warn);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    // Only resume plugins after any interrupted import has been fully rolled back.
                    SettingsBackup.RecoverInterruptedImports(dataDirectory);
                    message = TransferFailure(ex);
                }
            }
        }
        return SettingsBackup.OpenSession(dataDirectory, exclusive: false);
    }

    // The lease lives in a file another instance holds with FileShare.None, but the refusal reaches us as a
    // plain IOException wrapping the Win32 code (see DirectoryLockNativeMethods.OpenForBackup), so the code
    // has to be read off the chain rather than off HResult alone.
    private static bool IsSharingViolation(Exception exception)
    {
        for (var current = (Exception?)exception; current != null; current = current.InnerException)
        {
            if (current is System.ComponentModel.Win32Exception { NativeErrorCode: 32 or 33 }) return true;
            if (current.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021)) return true;
        }
        return false;
    }

    private static string TransferFailure(Exception exception)
    {
        Logger.Log($"[SettingsTransfer] {exception}", LogLevel.Error);
        var text = PluginSdk.Services.TranslationService.LoadEmbeddedTranslations(typeof(SettingsTransferRestart).Assembly, "en-US", "App");
        foreach (var pair in PluginSdk.Services.TranslationService.LoadEmbeddedTranslations(typeof(SettingsTransferRestart).Assembly,
                     System.Globalization.CultureInfo.CurrentUICulture.Name, "App")) text[pair.Key] = pair.Value;
        return string.Format(text["About_ConfigActionFailed"], exception.Message);
    }

    internal static string? ParseRequestId(IReadOnlyList<string> arguments)
    {
        var argument = arguments.SingleOrDefault(a => a.StartsWith(ArgumentPrefix, StringComparison.Ordinal));
        if (argument == null) return null;
        var id = argument[ArgumentPrefix.Length..];
        return Guid.TryParseExact(id, "N", out var parsed) ? parsed.ToString("N")
            : throw new InvalidDataException("Invalid settings transfer identifier.");
    }
}
