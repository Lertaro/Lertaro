using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.AudioDeviceSelector.CoreAudio;

namespace Lertaro.Plugins.AudioDeviceSelector;

public sealed class AudioDeviceSelectorInstantProvider : IInstantResultProvider
{
    private const string PluginId = "Lertaro.Plugins.AudioDeviceSelector";
    private const string DefaultTriggerKeyword = "ad";
    private const string SpeakerIcon = "M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z";
    private const string DefaultDeviceIcon = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z";

    private readonly CoreAudioDeviceProvider _deviceProvider = new();

    public string Name => TranslationService.Get("AudioDeviceSelector_ProviderName");

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        var keyword = GetTriggerKeyword();
        if (!TryParseQuery(query, keyword, out var searchTerm))
            return [];

        IReadOnlyList<AudioDeviceInfo> devices;
        try
        {
            devices = _deviceProvider.GetActiveRenderDevices();
        }
        catch (Exception ex)
        {
            Logger.Log($"[AudioDeviceSelector] Failed to enumerate playback devices: {ex.Message}", LogLevel.Error);
            return
            [
                new InstantResultItem
                {
                    Title = TranslationService.Get("AudioDeviceSelector_EnumerationFailed"),
                    Description = ex.Message,
                    IconData = SpeakerIcon,
                    IconColor = "AccentRed",
                    ActionType = "None"
                }
            ];
        }

        var displayMode = GetDisplayMode();
        var results = new List<InstantResultItem>();
        foreach (var device in devices)
        {
            var display = AudioDeviceDisplayFormatter.Format(device.FriendlyName, displayMode);
            if (!string.IsNullOrEmpty(searchTerm) &&
                !FuzzyMatchService.IsMatch(searchTerm, display.Title) &&
                !FuzzyMatchService.IsMatch(searchTerm, display.Description))
                continue;

            results.Add(new InstantResultItem
            {
                Title = display.Title,
                Description = string.IsNullOrEmpty(display.Description)
                    ? TranslationService.Get("AudioDeviceSelector_PluginName")
                    : display.Description,
                IconData = GetIconData(device.IsDefault),
                IconColor = GetIconColor(device.IsDefault),
                ActionType = "None",
                TabCompletion = $"{keyword} {display.Title}",
                OnExecuteFunc = () => SetDefaultDevice(device)
            });
        }

        return results;
    }

    public bool[]? GetHighlightMask(string text, string query)
    {
        var keyword = GetTriggerKeyword();
        return TryParseQuery(query, keyword, out var searchTerm) && !string.IsNullOrEmpty(searchTerm)
            ? FuzzyMatchService.GetHighlightMask(text, searchTerm)
            : null;
    }

    internal static bool TryParseQuery(string query, string keyword, out string searchTerm)
    {
        searchTerm = string.Empty;
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(keyword))
            return false;

        var trimmedQuery = query.Trim();
        var normalizedKeyword = keyword.Trim();
        if (trimmedQuery.Equals(normalizedKeyword, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = normalizedKeyword + " ";
        if (!trimmedQuery.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        searchTerm = trimmedQuery[ prefix.Length..].Trim();
        return true;
    }

    internal static string GetIconData(bool isDefault) => isDefault ? DefaultDeviceIcon : SpeakerIcon;

    internal static string GetIconColor(bool isDefault) => isDefault ? "SuccessBrush" : "AccentBlue";

    private static string GetTriggerKeyword() => PluginSettingsService.GetSetting(
        PluginId, "TriggerKeyword", DefaultTriggerKeyword).Trim() is { Length: > 0 } keyword
        ? keyword
        : DefaultTriggerKeyword;

    private static AudioDeviceDisplayMode GetDisplayMode()
    {
        var value = PluginSettingsService.GetSetting(PluginId, "DisplayMode", nameof(AudioDeviceDisplayMode.FriendlyName));
        return Enum.TryParse<AudioDeviceDisplayMode>(value, true, out var mode) ? mode : AudioDeviceDisplayMode.FriendlyName;
    }

    private bool SetDefaultDevice(AudioDeviceInfo device)
    {
        try
        {
            _deviceProvider.SetDefaultDevice(device.Id);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[AudioDeviceSelector] Failed to change playback device: {ex.Message}", LogLevel.Error);
            PluginMessageBoxService.Show(
                TranslationService.Get("AudioDeviceSelector_ChangeFailed"),
                TranslationService.Get("AudioDeviceSelector_PluginName"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return false;
        }
    }
}
