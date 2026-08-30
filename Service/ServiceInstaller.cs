using Lertaro.Core;

namespace Lertaro.Service;

// Windows service install/uninstall via sc.exe (through ServiceControlRunner), including the one-time
// security-descriptor change that lets the non-elevated App start/stop the service without a UAC prompt.
// Kept separate from Program's CLI dispatch and hook-mode bootstrap -- service lifecycle administration
// has nothing to do with either of those.
static class ServiceInstaller
{
    public static void Install()
    {
        try
        {
            var serviceExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lertaro.Service.exe");
            serviceExePath = Path.GetFullPath(serviceExePath);

            Console.WriteLine($"Installing service from path: {serviceExePath}");

            // Stop an existing service before changing its executable path. Updating it in place avoids
            // the SCM's asynchronous "marked for deletion" window that makes delete-then-create fragile.
            Logger.Log("Preparing existing service instance before install.");
            var stop = ServiceControlRunner.Run("stop LertaroService", 0, 1060, 1062);
            if (!stop.IsSuccess(0, 1060, 1062))
                throw new InvalidOperationException($"sc stop failed: {stop.Error}");
            if (!ServiceControlRunner.WaitForStopped("LertaroService"))
                throw new InvalidOperationException("sc stop timed out before the service reached STOPPED.");

            var serviceArguments = $"binPath= \"\\\"{serviceExePath}\\\" --service\" start= auto DisplayName= \"Lertaro Background Service\"";
            var configure = ServiceControlRunner.Run($"config LertaroService {serviceArguments}", 0, 1060);
            if (!configure.IsSuccess(0))
            {
                if (!configure.IsSuccess(1060))
                    throw new InvalidOperationException("sc config failed. See service.log for details.");

                Logger.Log($"Installing service: sc.exe create LertaroService {serviceArguments}");
                var create = ServiceControlRunner.Run($"create LertaroService {serviceArguments}");
                if (!create.IsSuccess(0))
                    throw new InvalidOperationException("sc create failed. See service.log for details.");
            }

            // Grant all authenticated users START/STOP/QUERY on the service so the non-elevated app can
            // start and stop it without a UAC prompt every time. Install is already elevated here, so this
            // one-time descriptor change is free. SYSTEM and Administrators keep full control.
            // AU ACE = CC LC SW RP WP LO RC = query-config/status, enum-deps, start, stop, interrogate, read.
            Logger.Log("Setting service security descriptor to allow non-admin start/stop.");
            var sdset = ServiceControlRunner.Run("sdset LertaroService D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)(A;;CCLCSWRPWPLORC;;;AU)S:(AU;FA;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;WD)");
            if (!sdset.IsSuccess(0))
                Logger.Log("[ServiceInstaller] Service was created but sdset failed; non-admin start/stop may require elevation.", LogLevel.Warn);

            Logger.Log("Starting service: sc.exe start LertaroService");
            var start = ServiceControlRunner.Run("start LertaroService", 0, 1056);
            if (!start.IsSuccess(0, 1056))
                throw new InvalidOperationException("sc start failed. See service.log for details.");

            Console.WriteLine("Service installed and started successfully!");
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            Console.WriteLine($"Failed to install service: {ex.Message}");
            Logger.Log($"Failed to install service: {ex}", LogLevel.Error);
        }
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
