using Lertaro.App.Services.Plugin;

namespace Lertaro.App.ViewModels.Settings.Plugins;

/// <summary>
/// Presents the measured diagnostics for one installed plugin.
/// </summary>
public sealed class PluginRuntimeStatusItemViewModel : ViewModelBase
{
    private readonly string _pluginId;

    public PluginRuntimeStatusItemViewModel(PluginInfoViewModel plugin)
    {
        Name = plugin.Name;
        _pluginId = plugin.DllFileName;
        Refresh();
    }

    public string Name { get; }
    public long InvocationCount { get; private set; }
    public double AverageElapsedMilliseconds { get; private set; }
    public double LastElapsedMilliseconds { get; private set; }
    public double MaxElapsedMilliseconds { get; private set; }
    public double AllocatedMegabytes { get; private set; }
    public long ExceptionCount { get; private set; }
    public bool HasData => InvocationCount > 0;

    public void Refresh()
    {
        var snapshot = PluginPerformanceMonitor.GetSnapshot(_pluginId);
        InvocationCount = snapshot.InvocationCount;
        AverageElapsedMilliseconds = snapshot.AverageElapsedMilliseconds;
        LastElapsedMilliseconds = snapshot.LastElapsedMilliseconds;
        MaxElapsedMilliseconds = snapshot.MaxElapsedMilliseconds;
        AllocatedMegabytes = snapshot.AllocatedMegabytes;
        ExceptionCount = snapshot.ExceptionCount;
        OnPropertyChanged(string.Empty);
    }
}
