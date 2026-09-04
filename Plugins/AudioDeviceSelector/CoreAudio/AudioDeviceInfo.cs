namespace Lertaro.Plugins.AudioDeviceSelector.CoreAudio;

internal sealed record AudioDeviceInfo(string Id, string FriendlyName, bool IsDefault);

internal enum AudioDeviceDisplayMode
{
    FriendlyName,
    DeviceName,
    DeviceDescription
}
