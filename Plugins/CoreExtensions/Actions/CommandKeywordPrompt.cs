using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Actions;

// Collects the single name a keyword-only command needs but was not given.
//
// Extracted (composition, not a partial class) because both "mkdir" and "touch" share it exactly, and
// because it documents one non-obvious point: the inline list offers these commands as soon as the
// KEYWORD matches, before any argument has been typed, so Execute routinely arrives with an empty
// argument. Returning silently in that case made Enter look broken -- the window closed and nothing
// happened -- so the missing name is asked for instead, mirroring RenameAction/AddFavoriteAction.
//
// Returns null when the user cancels or the host never wired PluginPromptService up; that is the
// caller's cue to do nothing, which is what the old code did unconditionally.
internal static class CommandKeywordPrompt
{
    public static string? Ask(string fieldKey, string actionLabelKey, string nameLabelKey)
    {
        var values = PluginPromptService.Prompt(
            TranslationService.Get(actionLabelKey),
            new[]
            {
                new PluginConfigField
                {
                    Key = fieldKey,
                    LabelKey = nameLabelKey,
                    FieldType = ConfigFieldType.Text,
                    RequireNonEmpty = true
                }
            });

        if (values == null || !values.TryGetValue(fieldKey, out var value) || value is not string name)
            return null;

        var trimmed = name.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
