namespace Lertaro.App.Views.Settings;

internal static class SettingsSidebarLayout
{
    internal const double CompactThreshold = 1000;
    internal const double CompactWidth = 64;
    internal static bool IsCompact(double windowWidth) => windowWidth <= CompactThreshold;
}
