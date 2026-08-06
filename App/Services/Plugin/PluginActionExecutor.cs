using Lertaro.Core;

using Lertaro.Core.Wire;
namespace Lertaro.App.Services.Plugin;

public static class PluginActionExecutor
{
    public static bool TryExecute(AppSearchResult result, PluginSdk.Abstractions.IPluginSearchWindow view, bool asAdmin = false)
    {
        // Apps (Start Menu shortcuts, packaged apps) launch through the same OnExecute delegate as an
        // instant result -- they just carry ResultKind "Application" instead so they get a real FullPath
        // and can be acted on (copy, locate in explorer, ...) like a normal file result.
        if (result.IsInstantResult || result.IsApplication)
        {
            // Dismiss the window before executing. An admin launch blocks on the UAC prompt, so
            // deferring the close (as the callers do on success) would leave the search window up
            // until the app actually starts. Closing up front makes it disappear immediately.
            view?.HideWindow();
            try
            {
                if (result.InstantResultOnExecute != null)
                {
                    if (asAdmin && !string.IsNullOrWhiteSpace(result.InstantResultActionArgument))
                        FileExecutor.OpenFileOrFolderAsAdmin(result.InstantResultActionArgument);
                    else
                        result.InstantResultOnExecute();
                }
                else if (result.InstantResultActionType == "Copy")
                {
                    System.Windows.Clipboard.SetText(result.InstantResultActionArgument);
                }
                else if (result.InstantResultActionType == "Execute")
                {
                    var arg = result.InstantResultActionArgument.Trim();
                    if (arg.StartsWith("cc_exec:", StringComparison.OrdinalIgnoreCase))
                    {
                        var json = arg.Substring(8).Trim();
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        var path = root.GetProperty("Path").GetString() ?? "";
                        var args = root.GetProperty("Arguments").GetString() ?? "";
                        var workingDir = root.GetProperty("WorkingDir").GetString() ?? "";
                        var runSilently = root.GetProperty("RunSilently").GetBoolean();
                        var targetRunAsAdmin = root.GetProperty("RunAsAdmin").GetBoolean();

                        var targetPsi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = path,
                            Arguments = args,
                            UseShellExecute = true
                        };
                        if (!string.IsNullOrWhiteSpace(workingDir))
                        {
                            targetPsi.WorkingDirectory = workingDir;
                        }
                        if (runSilently)
                        {
                            targetPsi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                            targetPsi.CreateNoWindow = true;
                        }
                        if (targetRunAsAdmin)
                        {
                            targetPsi.Verb = "runas";
                        }
                        System.Diagnostics.Process.Start(targetPsi);
                        return true;
                    }

                    if (arg.StartsWith("kill:", StringComparison.OrdinalIgnoreCase))
                    {
                        var pidStr = arg.Substring(5).Trim();
                        if (uint.TryParse(pidStr, out var pid))
                        {
                            App.HookClient?.SendMessage(new IpcMessage
                            {
                                Id = IpcMessageId.KillProcess,
                                ProcessId = pid
                            });
                        }
                        return true;
                    }

                    if (arg.StartsWith("activatewindow:", StringComparison.OrdinalIgnoreCase))
                    {
                        var hwndStr = arg.Substring("activatewindow:".Length).Trim();
                        if (long.TryParse(hwndStr, out var hwndValue) && hwndValue != 0)
                        {
                            // Bringing this target window forward is about to deactivate/hide our own
                            // window through one of THREE independent paths (this method's own HideWindow
                            // call above, QuickSearchWindow.Window_Deactivated's safety net, and
                            // QuickSearchWindowForegroundWatcher's global foreground hook) -- whichever
                            // one actually wins the FinishHide race would otherwise restore focus to
                            // _lastActiveHwnd (whatever was foreground before Lertaro was ever shown),
                            // undoing this activation a beat later. Suppress it up front, before
                            // ForceForeground triggers any of them.
                            (view as QuickSearchWindow)?.SuppressNextForegroundRestore();

                            // Reuses the same ForceForeground the search window's own hotkey-activation
                            // path uses to restore itself -- it's a generic "bring this HWND to the
                            // foreground, bypassing Windows' foreground-lock restriction" mechanism, not
                            // something specific to Lertaro's own windows. useAltTapBypass defaults to
                            // true, matching a keyboard Enter press here rather than a click already
                            // backed by very recent input on the Hook's own thread.
                            Views.QuickSearchWindow.Helpers.QuickSearchWindowController.ForceForeground(new IntPtr(hwndValue));
                        }
                        return true;
                    }

                    var runAsAdmin = false;
                    if (arg.StartsWith("runas:", StringComparison.OrdinalIgnoreCase))
                    {
                        runAsAdmin = true;
                        arg = arg.Substring(6).Trim();
                    }

                    var fileName = arg;
                    var arguments = "";
                    if (arg.StartsWith("\""))
                    {
                        var endQuote = arg.IndexOf('\"', 1);
                        if (endQuote > 0)
                        {
                            fileName = arg.Substring(1, endQuote - 1);
                            arguments = arg.Substring(endQuote + 1).Trim();
                        }
                    }
                    else
                    {
                        var firstSpace = arg.IndexOf(' ');
                        if (firstSpace > 0)
                        {
                            fileName = arg.Substring(0, firstSpace);
                            arguments = arg.Substring(firstSpace + 1).Trim();
                        }
                    }

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = true
                    };
                    if (runAsAdmin)
                    {
                        psi.Verb = "runas";
                    }
                    System.Diagnostics.Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[PluginActionExecutor] Failed to execute instant result action: {ex.Message}", LogLevel.Error);
            }
            return true;
        }

        if (!result.IsPluginSearchAction || result.IsSearchSectionHeader) return false;

        var registration = PluginManager.Instance.AllActions.FirstOrDefault(x => x.RuntimeActionId == result.PluginActionId);
        if (registration == null)
        {
            Logger.Log($"[PluginActionExecutor] Plugin search action not found: {result.PluginActionId}", LogLevel.Warn);
            return false;
        }

        registration.Action.Execute(
            new[] { new PluginSearchResult(result.Name, result.PluginActionArgumentText, result.ContextDirectory) }, view);
        return true;
    }
}
