using System.IO;

using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

using Microsoft.Win32;

using Application = System.Windows.Application;

namespace Lertaro.App.Services;

/// <summary>
/// Says out loud what Windows does not: a program in the <c>Run</c> key that needs elevation is skipped at
/// logon without launching, without a prompt and without an error, so a user who turned "start with Windows"
/// on sees a switch that reads as working and a launcher that never appears.
/// </summary>
/// <remarks>
/// The mark is nothing this app does -- the installer hands the App back with <c>runasoriginaluser</c>, the
/// service relaunches it after an update with <c>requestElevation: false</c>, and no line of this repository
/// writes <c>AppCompatFlags\Layers</c>. It is the Compatibility tab's "run this program as an administrator",
/// which lands in HKCU for one user and in HKLM when ticked for everyone, or a policy that set the same value.
/// Lertaro therefore does not offer to undo it: it is a choice somebody made deliberately, the HKLM copy needs
/// an administrator to change at all, and a notification's click and close are the same event to the host, so
/// there is no way to hang an action on "the user asked for this" without also firing it on a dismissal.
/// </remarks>
internal static class StartupElevationNotice
{
    private const string LayersKeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string RunAsAdminFlag = "RUNASADMIN";

    /// <summary>Call once per start, after the <c>Run</c> key has been written, and only when it was switched on.</summary>
    internal static void WarnIfMarkedElevated()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        if (!IsMarkedElevated(RegistryHive.CurrentUser, exe) && !IsMarkedElevated(RegistryHive.LocalMachine, exe))
            return;

        Logger.Log("[StartupElevation] Autostart is switched on but this exe is marked to run elevated; "
            + "Windows skips such a Run entry at logon, silently.", LogLevel.Warn);

        // Deferred a dispatcher turn: this runs inside OnStartup, before any window exists, and the card is a
        // window. ApplicationIdle also lets anything else the boot queues up take its slot in order instead of
        // racing this one for the screen.
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => Notifications.NotificationService.Show(new NotificationRequest
        {
            Id = "startup-elevation-marked",
            Title = TranslationManager.Instance["Startup_SkippedTitle"],
            Message = TranslationManager.Instance["Startup_SkippedBody"],
            Level = NotificationLevel.Warn,
            // Longer than the stack's own default: at logon nobody is looking at the screen yet, and this
            // is the only moment the reason their launcher did not appear gets said.
            DurationSeconds = 20,
        }, typeof(StartupElevationNotice).Assembly)), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static bool IsMarkedElevated(RegistryHive hive, string exePath)
    {
        try
        {
            // Registry64 rather than the default view: a 32-bit build would otherwise be handed the WOW
            // redirect of a key the shell only ever writes in the 64-bit one, and would miss the mark.
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var layers = baseKey.OpenSubKey(LayersKeyPath);
            if (layers == null) return false;

            foreach (var name in layers.GetValueNames())
            {
                if (!IsThisExe(name, exePath)) continue;
                if (layers.GetValue(name) is string data && RequestsElevation(data)) return true;
            }
        }
        catch (Exception ex)
        {
            // A denied or malformed read is not a reason to claim anything; staying quiet about the hive and
            // about the reason is what keeps this from becoming a second bug report.
            Logger.Log($"[StartupElevation] Could not read the {hive} compatibility layers: {ex.Message}", LogLevel.Debug);
        }

        return false;
    }

    // The value name is the path as whoever wrote it spelled it -- quoted, unquoted or with an expanded
    // environment block -- so it is compared as a path.
    // ponytail: an 8.3 form (PROGRA~1) does not normalize to the long path and reads as someone else's entry;
    // the upgrade is GetLongPathName before comparing. Nothing in Windows writes that form here today.
    internal static bool IsThisExe(string valueName, string exePath)
    {
        var trimmed = valueName.Trim().Trim('"');
        if (trimmed.Length == 0) return false;
        try
        {
            return string.Equals(Path.GetFullPath(trimmed), exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The data is a space-separated flag list, typically <c>~ RUNASADMIN</c>.</summary>
    internal static bool RequestsElevation(string data) =>
        data.Contains(RunAsAdminFlag, StringComparison.OrdinalIgnoreCase);
}
