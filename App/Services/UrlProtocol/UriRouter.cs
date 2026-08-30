using Lertaro.Core;

using Lertaro.App.Helpers.LocalSend;
using Lertaro.App.Services.AppWindow;
using Lertaro.App.ViewModels.LocalSend;
namespace Lertaro.App.Services.UrlProtocol;

// Routes a "lertaro://" URI to the matching in-app action. Reached from two places (see App.xaml.cs):
// this process's own launch args, when the OS invoked Lertaro directly via the link; or forwarded
// through AppPipeService from a second-instance launch, when Lertaro was already running. Every route
// touches a Window, so dispatching onto the UI thread is mandatory here rather than left to callers.
// Unrecognized/malformed input takes no action beyond logging -- the protocol is registered system-wide
// (see UrlProtocolManager), so any process can invoke it with anything, and a bad or unknown link should
// never surprise the user with unexpected behavior.
public static class UriRouter
{
    public static bool IsLertaroUri(string? candidate) =>
        Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Scheme.Equals("lertaro", StringComparison.OrdinalIgnoreCase);

    public static void Route(string uriString)
    {
        if (uriString.Length > LocalSendUriParser.MaxUriLength
            || !Uri.TryCreate(uriString, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals("lertaro", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Log($"[UriRouter] Ignoring malformed/non-lertaro URI: {uriString}", LogLevel.Warn);
            return;
        }

        var route = uri.Host;
        var arg = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            switch (route.ToLowerInvariant())
            {
                case "":
                    (System.Windows.Application.Current.MainWindow as QuickSearchWindow)?.ShowWindow();
                    break;

                case "search":
                    // An empty search URI is still an explicit launch request. Passing an empty string
                    // keeps it from being mistaken for the ordinary hotkey summon, which may import the
                    // clipboard when no query was supplied.
                    (System.Windows.Application.Current.MainWindow as QuickSearchWindow)?.ShowWindow(arg);
                    break;

                case "fullsearch":
                    FileExecutor.OpenFileOrFolder("__SHOW_MORE__", arg);
                    break;

                case "settings":
                    RouteSettings(arg, uriString);
                    break;

                case "localsend":
                    RouteLocalSend(uri, uriString);
                    break;

                default:
                    Logger.Log($"[UriRouter] Unknown route: {uriString}", LogLevel.Warn);
                    break;
            }
        }));
    }

    private static void RouteLocalSend(Uri uri, string uriString)
    {
        if (!LocalSendUriParser.TryParse(uri, out var request) || request == null)
        {
            Logger.Log($"[UriRouter] Invalid LocalSend route: {uriString}", LogLevel.Warn);
            return;
        }

        if (!UserSettings.Load().LocalSend.Enabled)
        {
            AppWindowManager.ShowSettingsWindow("LocalSend");
            return;
        }

        var mode = request.Kind switch
        {
            LocalSendUriRequestKind.Items => LocalSendSendMode.Items,
            LocalSendUriRequestKind.Text => LocalSendSendMode.Text,
            _ => (LocalSendSendMode?)null
        };
        LocalSendAppEventHandler.OpenSendWindow(request.Files, request.Text, mode, ignoreIfOpen: true);
    }

    // "page/<section>" switches to a top-level sidebar section (e.g. "page/Index"), matching the tag
    // values SettingsWindow.SelectSection already accepts. "entry/<index>" jumps straight to one
    // specific setting (section + tab + row highlight) -- index into SettingsSearchIndex.Entries, the
    // same list PluginSdk.Services.SettingsSearchService exposes to plugins.
    private static void RouteSettings(string arg, string uriString)
    {
        if (arg.Length == 0)
        {
            AppWindowManager.ShowSettingsWindow();
            return;
        }

        var segments = arg.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 2 && segments[0].Equals("page", StringComparison.OrdinalIgnoreCase))
        {
            AppWindowManager.ShowSettingsWindow(segments[1]);
            return;
        }

        if (segments.Length == 2 && segments[0].Equals("entry", StringComparison.OrdinalIgnoreCase) && int.TryParse(segments[1], out var entryIndex))
        {
            AppWindowManager.ShowSettingsWindowEntry(entryIndex);
            return;
        }

        Logger.Log($"[UriRouter] Unknown settings sub-route: {uriString}", LogLevel.Warn);
    }
}
