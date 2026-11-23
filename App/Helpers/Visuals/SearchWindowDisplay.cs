namespace Lertaro.App.Helpers.Visuals;

internal static class SearchWindowDisplay
{
    internal static Screen Resolve(string? deviceName)
    {
        var screens = Screen.AllScreens;
        var index = ResolveDisplayIndex(deviceName, screens.Select(screen => screen.DeviceName).ToArray());
        return index >= 0 ? screens[index] : Screen.FromPoint(Control.MousePosition);
    }

    // A disconnected display falls back to the cursor without changing the saved preference.
    internal static int ResolveDisplayIndex(string? deviceName, string[] availableDevices) =>
        string.IsNullOrEmpty(deviceName) ? -1
            : Array.FindIndex(availableDevices, available => string.Equals(available, deviceName, StringComparison.OrdinalIgnoreCase));
}
