namespace Lertaro.Core.DriveMonitoring;

internal sealed class DriveMonitorRegistration : IDisposable
{
    private readonly IDisposable _monitor;
    private readonly DriveDeviceRemovalMonitor? _removalMonitor;

    public DriveMonitorRegistration(IDisposable monitor, DriveDeviceRemovalMonitor? removalMonitor)
    {
        _monitor = monitor;
        _removalMonitor = removalMonitor;
    }

    public void DisposeMonitor() => _monitor.Dispose();

    public void Dispose()
    {
        _monitor.Dispose();
        _removalMonitor?.Dispose();
    }
}
