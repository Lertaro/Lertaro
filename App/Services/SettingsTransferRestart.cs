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
        message = null;
        var requestId = ParseRequestId(arguments);
        FileStream exclusive;
        try { exclusive = SettingsBackup.OpenSession(dataDirectory, exclusive: true); }
        catch (IOException) when (requestId == null)
        {
            return SettingsBackup.OpenSession(dataDirectory, exclusive: false);
        }
        using (exclusive)
        {
            // Resolve the existing migration path before snapshotting, without loading settings objects.
            if (migrate) _ = UserSettings.SettingsPath;
            SettingsBackup.RecoverInterruptedImports(dataDirectory);
            if (requestId != null)
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
        }
        return SettingsBackup.OpenSession(dataDirectory, exclusive: false);
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
