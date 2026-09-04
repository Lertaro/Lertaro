namespace Lertaro.Plugins.AudioDeviceSelector.CoreAudio;

internal static class AudioDeviceDisplayFormatter
{
    internal static (string Title, string Description) Format(string friendlyName, AudioDeviceDisplayMode mode)
    {
        var (deviceName, deviceDescription) = SplitFriendlyName(friendlyName);
        return mode switch
        {
            AudioDeviceDisplayMode.DeviceName => (deviceName, deviceDescription),
            AudioDeviceDisplayMode.DeviceDescription => (deviceDescription, deviceName),
            _ => (friendlyName, string.Empty)
        };
    }

    internal static (string Name, string Description) SplitFriendlyName(string friendlyName)
    {
        if (string.IsNullOrWhiteSpace(friendlyName))
            return (friendlyName, string.Empty);

        var closingParenthesis = friendlyName.Length - 1;
        var openingParenthesis = friendlyName.LastIndexOf('(');
        if (openingParenthesis <= 0 || friendlyName[closingParenthesis] != ')' || openingParenthesis == closingParenthesis - 1)
            return (friendlyName, string.Empty);

        var name = friendlyName[..openingParenthesis].TrimEnd();
        var description = friendlyName[(openingParenthesis + 1)..closingParenthesis].Trim();
        return string.IsNullOrEmpty(name) || string.IsNullOrEmpty(description)
            ? (friendlyName, string.Empty)
            : (name, description);
    }
}
