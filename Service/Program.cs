using System.ServiceProcess;

using Lertaro.Core;
using Lertaro.Core.Services.Installation;

namespace Lertaro.Service;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Set up global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Logger.Log($"CRITICAL SERVICE UNHANDLED EXCEPTION:\n{e.ExceptionObject}", LogLevel.Error);

        // Wire up plugin logger to the core logger
        PluginSdk.Logger.LogAction = (msg, lvl) => Logger.Log(msg, (LogLevel)(int)lvl);

        var isHook = args.Length > 0 && args[0].Equals("--hook", StringComparison.OrdinalIgnoreCase);
        if (isHook)
        {
            Logger.Initialize("hook.log", Logger.UserDataDir, overwrite: true);
            Logger.Log("=========================================");
            Logger.Log($"Hook starting with arguments: {string.Join(" ", args)}");
        }
        else
        {
            // Before the log is opened: the service writes this directory as LocalSystem, and until it is
            // locked any user may have planted a link in it that redirects exactly that write.
            var repair = args.Length > 0 && args[0].Equals("--repair-permissions", StringComparison.OrdinalIgnoreCase);
            var isService = args.Length > 0 && args[0].Equals("--service", StringComparison.OrdinalIgnoreCase);
            // This path has no log of its own (see PermissionRepairReport), so every step is recorded before it
            // runs: a repair that is killed for running too long leaves the step it was stuck on as the last line.
            if (repair) PermissionRepairReport.Write($"repair starting ({string.Join(' ', args)})");
            var (lockReport, lockErrors) = LockDirectories(isService: isService || repair);
            var install = args.Length > 0 && args[0] is "--install" or "-i";
            if ((isService || repair || install) && (lockReport.Failed.Count > 0 || lockErrors.Count > 0))
            {
                // Do not open logs, indexes or plugins through a tree whose ACL preparation failed.
                var failure = string.Join(Environment.NewLine, lockReport.Failed.Concat(lockErrors));
                if (repair) PermissionRepairReport.Write($"repair failed while locking directories: {failure}");
                Console.Error.WriteLine(failure);
                Environment.ExitCode = 1;
                return;
            }
            if (repair)
            {
                try
                {
                    if (args.Length != 2 || !UserProfiles.IsAccount(args[1]) ||
                        !UserProfiles.Read().TryGetValue(args[1], out var profile))
                        throw new UnauthorizedAccessException("Unknown settings owner.");
                    var user = new System.Security.Principal.SecurityIdentifier(args[1]);
                    var failures = new List<string>();

                    // Every root this copy could be about to use, both derived from the profile map and this
                    // process's own location -- never an arbitrary path supplied by a low-integrity process.
                    // A portable copy keeps its user data beside its executable, so repairing only the profile
                    // default walked a folder the App never reads and left the one it does read untouched.
                    foreach (var directory in PortableDirectoryLock.RepairTargets(InstallationDetector.Detect(),
                                 AppContext.BaseDirectory,
                                 Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                 profile, args[1]))
                    {
                        if (!Directory.Exists(directory))
                        {
                            PermissionRepairReport.Write($"{directory} does not exist; nothing to repair.");
                            continue;
                        }

                        PermissionRepairReport.Write($"repairing {directory}");
                        var repaired = PortableDirectoryLock.RepairUserDataDirectory(directory, profile, user);
                        if (PortableDirectoryLock.FatalReason(repaired) is { } fatal) failures.Add(fatal);
                        // A personal file another process is holding open is not the trust boundary, and
                        // elevation cannot open it anyway, so it is recorded rather than failing the repair:
                        // one busy settings file must not be the reason the App cannot start. The App
                        // re-checks the directory itself once this returns (UserDataAccess.Verify), and that
                        // is the verdict which actually decides whether it can run.
                        if (repaired.UserDataFailed.Count > 0)
                            PermissionRepairReport.Write(
                                $"left as it is, in use: {string.Join(Environment.NewLine, repaired.UserDataFailed)}");
                    }

                    if (failures.Count > 0) throw new IOException(string.Join(Environment.NewLine, failures));
                    PermissionRepairReport.Write("repair completed");
                }
                catch (Exception ex)
                {
                    // Written back for the App to show: with UseShellExecute + runas its stderr cannot be
                    // redirected, and the service log is not open yet on this path.
                    PermissionRepairReport.Write($"repair failed: {ex.Message}");
                    Console.Error.WriteLine(ex.Message);
                    Environment.ExitCode = 1;
                }
                return;
            }
            Logger.Initialize("service.log", Logger.SharedDataDir, overwrite: true);
            // Before the first line, so the level applies to everything this run writes. The service is
            // the one process that cannot read the per-user log-level setting -- it runs as LocalSystem
            // and that setting lives under the interactive user's %LocalAppData% -- so it had none at
            // all, and every LogLevel.Debug line in the indexer was unreachable whatever the settings
            // page said. See MachineSettings.ServiceLogLevel.
            Logger.MinimumLevel = MachineSettings.Load().ResolveServiceLogLevel();
            Logger.Log("=========================================");
            Logger.Log($"Service starting with arguments: {string.Join(" ", args)}");
            foreach (var path in lockReport.Removed)
                Logger.Log($"[InstallDirectoryLock] Removed a link that was not this product's: {path}", LogLevel.Warn);
            foreach (var failure in lockReport.Failed)
                Logger.Log($"[InstallDirectoryLock] Could not reset {failure}", LogLevel.Error);
            foreach (var failure in lockReport.UserDataFailed)
                Logger.Log($"[InstallDirectoryLock] Personal data repair incomplete: {failure}", LogLevel.Warn);
            foreach (var error in lockErrors)
                Logger.Log($"[InstallDirectoryLock] Could not lock {error}", LogLevel.Error);
        }

        if (args.Length > 0)
        {
            var cmd = args[0].ToLowerInvariant();
            if (cmd == "--service")
            {
                ServiceInstaller.ApplySecurity();
                Logger.Log("Running as Windows Service.");
                ServiceBase.Run(new UsnService());
                return;
            }
            else if (cmd == "--install" || cmd == "-i")
            {
                Logger.Log("Executing service installation.");
                ServiceInstaller.Install();
                return;
            }
            else if (cmd == "--uninstall" || cmd == "-u")
            {
                Logger.Log("Executing service uninstallation.");
                ServiceInstaller.Uninstall();
                return;
            }
            else if (cmd == "--hook")
            {
                Logger.Log("Running in hook mode.");
                HookModeLauncher.Run();
                // Run() has disposed the hook and pipes; plugin-owned foreground threads must not
                // keep this per-user process alive after its host requests a stop/restart.
                Environment.Exit(0);
                return;
            }
        }

        // Default fallback: Debug Console Mode
        Logger.Log("Running in debug console mode.");
        Console.WriteLine("Lertaro Background Service is running. Press Ctrl+C to exit.");

        using var service = new UsnServiceDebugWrapper();
        service.Start();

        var quitEvent = new ManualResetEvent(false);
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            eventArgs.Cancel = true;
            quitEvent.Set();
        };
        quitEvent.WaitOne();
        service.Stop();
    }

    // Only a process running as LocalSystem or elevated can do this; the debug console run by a plain user
    // gets the error back and carries on, as it always has, with a directory it can write.
    //
    // The service also re-locks a portable copy's own folder when that is no longer locked the way this build
    // locks it: updates are applied in place without another --install, so this is the only point at which a
    // copy installed before the lock existed, or locked by 5.8.2 (issue #316), gets the current one.
    private static (InstallDirectoryLock.Report Report, List<string> Errors) LockDirectories(bool isService)
    {
        var report = new InstallDirectoryLock.Report();
        var errors = new List<string>();

        Lock(Logger.SharedDataDir, () => InstallDirectoryLock.PrepareSharedDataDirectory(Logger.SharedDataDir));
        var appDirectory = AppContext.BaseDirectory;
        if (isService && ServiceInstaller.LocksApplicationFolder)
            Lock(appDirectory, () => PortableDirectoryLock.IsCurrent(appDirectory) ? null : PortableDirectoryLock.Lock(appDirectory));
        return (report, errors);

        void Lock(string directory, Func<InstallDirectoryLock.Report?> lockIt)
        {
            try
            {
                if (lockIt() is { } locked)
                {
                    report.Removed.AddRange(locked.Removed);
                    report.Failed.AddRange(locked.Failed);
                    report.UserDataFailed.AddRange(locked.UserDataFailed);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{directory}: {ex.Message}");
            }
        }
    }
}

class UsnServiceDebugWrapper : IDisposable
{
    private readonly UsnService _service = new UsnService();
    public void Start() => _service.TestStart();
    public void Stop() => _service.TestStop();
    public void Dispose() => _service.Dispose();
}
