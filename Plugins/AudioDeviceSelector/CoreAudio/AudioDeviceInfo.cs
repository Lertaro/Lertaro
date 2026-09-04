namespace Lertaro.Plugins.AudioDeviceSelector.CoreAudio;

internal sealed record AudioDeviceInfo(string Id, string FriendlyName);

internal enum AudioDeviceDisplayMode
{
    FriendlyName,
    DeviceName,
    DeviceDescription
}
