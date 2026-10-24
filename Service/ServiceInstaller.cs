using Lertaro.Core;
using Lertaro.Core.Services.Installation;

namespace Lertaro.Service;

// Windows service install/uninstall via sc.exe (through ServiceControlRunner), including the one-time
// security-descriptor change that lets a signed-in user start/query the service without a UAC prompt.
// Kept separate from Program's CLI dispatch and hook-mode bootstrap -- service lifecycle administration
// has nothing to do with either of those.
static class ServiceInstaller
{
#if DEBUG
    private const bool IsDebugBuild = true;
#else
    private const bool IsDebugBuild = false;
#endif

    // ponytail: not in a Debug build, which is a developer's build output (build_and_run.bat's debug\) that the
    // next non-elevated build has to delete and rewrite. Release zips are what ship.
    internal static bool LocksApplicationFolder => !IsDebugBuild;

    public static void Install()
    {
        try
        {
            var serviceExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lertaro.Service.exe");
            serviceExePath = Path.GetFullPath(serviceExePath);

            Console.WriteLine($"Installing service from path: {serviceExePath}");
            // Recorded before each step: the App reads this file back when this process exits non-zero, and the
            // step that was running is the answer. Nothing else survives -- this runs elevated with a hidden
            // window, and a failure here is why a service never appears at all.
            PermissionRepairReport.Write($"installing from {serviceExePath}");

            // A portable copy lives wherever it was unzipped, usually somewhere every user can write, and the
            // service about to run from it as LocalSystem would load whatever anyone put there. Locked before
            // the service is pointed at it; a folder that cannot be locked is not one to install it from.
            if (LocksApplicationFolder)
            {
                PermissionRepairReport.Write($"locking the portable folder {Path.GetDirectoryName(serviceExePath)}");
                Logger.Log("Locking the portable folder to SYSTEM and Administrators before installing the service.");
                var report = PortableDirectoryLock.Lock(Path.GetDirectoryName(serviceExePath)!);
                foreach (var path in report.Removed)
                    Logger.Log($"[InstallDirectoryLock] Removed a link that was not this product's: {path}", LogLevel.Warn);
                foreach (var path in report.UserDataFailed)
                    Logger.Log($"[InstallDirectoryLock] Personal data repair incomplete: {path}", LogLevel.Warn);
                if (report.Failed.Count > 0)
                    throw new InvalidOperationException($"Could not lock the portable folder: {string.Join("; ", report.Failed)}");
            }

            // Stop an existing service before changing its executable path. Updating it in place avoids
            // the SCM's asynchronous "marked for deletion" window that makes delete-then-create fragile.
            PermissionRepairReport.Write("stopping any service that is already registered");
            Logger.Log("Preparing existing service instance before install.");
            var stop = ServiceControlRunner.Run("stop LertaroService", 0, 1060, 1062);
            if (!stop.IsSuccess(0, 1060, 1062))
                throw new InvalidOperationException($"sc stop failed: {stop.Error}");
            if (!ServiceControlRunner.WaitForStopped("LertaroService"))
                throw new InvalidOperationException("sc stop timed out before the service reached STOPPED.");

            // No start type here: sc create takes no `delayed-auto`, so the start type is settled once for both
            // paths by ApplyDelayedStart below.
            var serviceArguments = $"binPath= \"\\\"{serviceExePath}\\\" --service\" DisplayName= \"Lertaro Background Service\"";
            PermissionRepairReport.Write("registering or reconfiguring the service");
            var configure = ServiceControlRunner.Run($"config LertaroService {serviceArguments}", 0, 1060);
            if (!configure.IsSuccess(0))
            {
                if (!configure.IsSuccess(1060))
                    throw new InvalidOperationException("sc config failed. See service.log for details.");

                Logger.Log($"Installing service: sc.exe create LertaroService {serviceArguments} start= auto");
                var create = ServiceControlRunner.Run($"create LertaroService {serviceArguments} start= auto");
                if (!create.IsSuccess(0))
                    throw new InvalidOperationException("sc create failed. See service.log for details.");
            }

            ApplyDelayedStart();
            ApplySecurity();
            ApplyRecovery();

            PermissionRepairReport.Write("starting the service");
            Logger.Log("Starting service: sc.exe start LertaroService");
            var start = ServiceControlRunner.Run("start LertaroService", 0, 1056);
            if (!start.IsSuccess(0, 1056))
                throw new InvalidOperationException("sc start failed. See service.log for details.");

            PermissionRepairReport.Write("installed and started");
            Console.WriteLine("Service installed and started successfully!");
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            Console.WriteLine($"Failed to install service: {ex.Message}");
            PermissionRepairReport.Write($"install failed: {ex.Message}");
            Logger.Log($"Failed to install service: {ex}", LogLevel.Error);
        }
    }

    /// <summary>
    /// Makes the service start after the boot-time logon storm instead of inside it. An ordinary AUTO_START
    /// service competes with everything else that happens at boot for the same disk, and on the slowest
    /// machines that is what loses the SCM's 30-second start window: the service is declared dead, its process
    /// is left running, and local search stays down until someone starts it by hand.
    /// </summary>
    /// <remarks>
    /// Run on both install paths rather than baked into the create arguments because sc create does not accept
    /// `delayed-auto`, and because a service registered by an older build is never re-created: it is only ever
    /// reconfigured, and this is the step that converges it. Deliberately not re-applied on every start, unlike
    /// the two below -- a start type is a choice someone can make for their machine, and a service that is
    /// already running has no business overruling it. A refusal here leaves the plain auto start type in place,
    /// which still starts; failing the whole install over it would be the worse outcome.
    /// </remarks>
    internal static void ApplyDelayedStart()
    {
        var result = ServiceControlRunner.Run("config LertaroService start= delayed-auto");
        if (!result.IsSuccess(0))
            Logger.Log($"[ServiceInstaller] Could not set the delayed automatic start: {result.Error}", LogLevel.Warn);
    }

    /// <summary>
    /// Lets the SCM retry a start that failed. A service with no recovery actions stays stopped after one loss,
    /// so a single lost 30-second window became "local search is dead until the user opens the settings page" --
    /// and on a machine that boots slowly, that window is lost often.
    /// </summary>
    /// <remarks>
    /// Reapplied on every start, like <see cref="ApplySecurity"/>: an in-place upgrade does not run --install,
    /// so this is the only route by which a service registered by an older build gets retry behavior at all.
    /// Not a failure boundary -- a machine that refuses this still starts, it just loses the retries -- so a
    /// refusal is logged rather than thrown.
    /// </remarks>
    internal static void ApplyRecovery()
    {
        var result = ServiceControlRunner.Run(
            "failure LertaroService reset= 86400 actions= restart/15000/restart/30000/restart/60000");
        if (!result.IsSuccess(0))
            Logger.Log($"[ServiceInstaller] Could not set the service recovery actions: {result.Error}", LogLevel.Warn);
    }

    internal static void ApplySecurity()
    {
        // A user may start the shared search service, but cannot stop it for every other session.
        // Reapply on service startup too: in-place upgrades do not run --install.
        var result = ServiceControlRunner.Run("sdset LertaroService D:(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)(A;;CCLCSWRPLORC;;;AU)");
        if (!result.IsSuccess(0)) throw new IOException("Could not apply the service access policy.");
    }

    public static void Uninstall()
    {
        try
        {
            Logger.Log("Stopping service: sc.exe stop LertaroService");
            var stop = ServiceControlRunner.Run("stop LertaroService", 0, 1060, 1062);
            if (!stop.IsSuccess(0, 1060, 1062))
                throw new InvalidOperationException($"sc stop failed: {stop.Error}");
            if (!ServiceControlRunner.WaitForStopped("LertaroService"))
                throw new InvalidOperationException("sc stop timed out before the service reached STOPPED.");

            Logger.Log("Deleting service: sc.exe delete LertaroService");
            var delete = ServiceControlRunner.Run("delete LertaroService", 0, 1060);
            if (!delete.IsSuccess(0, 1060))
                throw new InvalidOperationException("sc delete failed. See service.log for details.");
            if (!ServiceControlRunner.WaitForDeleted("LertaroService"))
                throw new InvalidOperationException("sc delete timed out before the service was removed.");

            Console.WriteLine("Service uninstalled successfully!");
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            Console.WriteLine($"Failed to uninstall service: {ex.Message}");
            Logger.Log($"Failed to uninstall service: {ex}", LogLevel.Error);
        }
    }
}
