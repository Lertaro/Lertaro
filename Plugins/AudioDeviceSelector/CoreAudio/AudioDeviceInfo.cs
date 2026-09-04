namespace Lertaro.Plugins.AudioDeviceSelector.CoreAudio;

internal sealed record AudioDeviceInfo(
    string Id,
    string FriendlyName,
    AudioDeviceDirection Direction,
    bool IsDefault);

internal enum AudioDeviceDirection
{
    Output,
    Input
}

internal enum AudioDeviceDisplayMode
{
    FriendlyName,
    DeviceName,
    DeviceDescription
}
