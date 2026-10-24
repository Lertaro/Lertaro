namespace Lertaro.App.Services;

// Split out purely to keep ServiceInstallManager.cs under the repo's per-file line limit. It holds no state and
// takes the state probe as a parameter, so the decision is testable without a registered service.
internal static class ServiceStartSettleHelper
{
    /// <summary>
    /// Waits while the Service Control Manager still has a start of LertaroService in flight, and reports
    /// whether the start settled within <paramref name="timeoutMs"/>.
    /// </summary>
    /// <remarks>
    /// The elevated installer stops the service and re-ACLs its own directory tree as its first two steps.
    /// Doing that to a process that has not finished starting is how one slow boot ends as exit code 1 with no
    /// service registered at all -- the reported failure was a service that needed longer than the App was
    /// willing to wait, reinstalled underneath itself, and never came back. A start that is merely slow is
    /// therefore waited out instead of reinstalled. The bound sits past the SCM's own 30-second window, because
    /// past that point the service has been told it failed and a reinstall is the right answer again.
    /// </remarks>
    internal static bool Wait(Func<bool> isStartPending, int timeoutMs, int pollMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (isStartPending())
        {
            if (Environment.TickCount64 >= deadline)
                return false;

            Thread.Sleep(pollMs);
        }

        return true;
    }
}
