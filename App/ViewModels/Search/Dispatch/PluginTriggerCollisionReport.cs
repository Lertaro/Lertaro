using Lertaro.Core;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.ViewModels.Search.Dispatch;

/// <summary>
/// Who else already answers to a trigger word: the rule behind the amber warning under a Settings field and
/// the one-line log entry a collision produces.
/// </summary>
/// <remarks>
/// Split out purely to keep PluginTriggerQuery under the repo's per-file line limit; this class has no state
/// of its own beyond the log gate, and it always operates on the inventory PluginTriggerQuery collected --
/// entries in, owner name out -- rather than collecting one of its own, because two collectors would drift
/// and the warning would stop describing what the strip actually does.
/// </remarks>
internal static class PluginTriggerCollisionReport
{
    // The host's own per-type trigger characters are one "component" as far as a clash is concerned.
    internal const string HostTriggerOwnerId = "Lertaro.Settings.ResultTypeTriggers";

    /// <summary>
    /// The other feature that already answers to <paramref name="word"/> -- its display name, or null when
    /// the word is free. <paramref name="selfOwnerId"/> is the asking component's own id: a plugin reusing
    /// one word across two of its own fields is not a clash between features, since the user sees one list
    /// of results either way.
    /// </summary>
    public static string? FindOtherOwner(string word, string selfOwnerId) =>
        FirstOtherOwner(PluginTriggerQuery.Collect(), word, selfOwnerId);

    /// <summary>
    /// The other feature that already answers to <paramref name="word"/>, for the host's own per-type
    /// trigger characters. Those live in one settings table and are therefore one "component" as far as
    /// <see cref="FirstOtherOwner"/> is concerned, so the id it compares against is supplied here: a
    /// character that collides with a plugin's trigger word is still worth naming, and a caller outside this
    /// class must not have to know the host's own id to ask.
    /// </summary>
    public static string? FindOtherOwnerForHostTrigger(string word) =>
        FirstOtherOwner(PluginTriggerQuery.Collect(), word, HostTriggerOwnerId);

    /// <summary>
    /// Who else already owns this word, or null when it is free. Pure, so a Settings field can warn on the
    /// same rule the log line uses instead of a second approximation of it.
    /// </summary>
    internal static string? FirstOtherOwner(IReadOnlyList<PluginTriggerQuery.Entry> entries, string word, string selfOwner)
    {
        if (string.IsNullOrWhiteSpace(word))
            return null;

        var candidate = TriggerWord.Normalize(word);
        foreach (var entry in entries)
        {
            // An owner matching itself is a plugin reusing its own word across two of its own fields, not a
            // clash between features -- the user sees one list of results either way. Compared by component
            // identity when the entry carries one (that is what the Settings page can supply: it knows a
            // plugin's id, never the localized name an entry shows), falling back to the display name.
            var isSelf = entry.OwnerId.Length > 0
                ? string.Equals(entry.OwnerId, selfOwner, StringComparison.Ordinal)
                : string.Equals(entry.Owner, selfOwner, StringComparison.Ordinal);
            if (isSelf)
                continue;
            if (string.Equals(entry.Word, candidate, StringComparison.OrdinalIgnoreCase))
                return entry.Owner;
        }

        return null;
    }

    // One line per distinct set of collisions: a page refresh or a keystroke must not repeat the same
    // warning forever while the user has not decided what to rename. Guarded, because remembering it is a
    // read-compare-write on one piece of state and a search can be dispatched from more than one thread --
    // unsynchronised, two keystrokes can both see the old value and log the identical line. The decision is
    // taken under the lock and the write happens outside it, so no I/O runs while it is held.
    private static readonly object WarningGate = new();
    private static string? _warnedSignature;

    internal static void WarnAboutCollisions(IReadOnlyList<PluginTriggerQuery.Entry> entries)
    {
        var report = new List<string>();
        foreach (var entry in entries)
        {
            var other = FirstOtherOwner(entries, entry.Word, entry.OwnerId);
            // Report each pair once, from the owner that sorts first.
            if (other != null && string.CompareOrdinal(entry.Owner, other) < 0)
                report.Add($"{entry.Word}' ({entry.Owner} / {other})");
        }

        string? signatureToLog = null;
        lock (WarningGate)
        {
            if (report.Count == 0)
                _warnedSignature = null;
            else
            {
                var signature = string.Join("|", report);
                if (signature != _warnedSignature)
                {
                    _warnedSignature = signature;
                    signatureToLog = signature;
                }
            }
        }

        if (signatureToLog != null)
            Logger.Log($"[PluginTriggerQuery] more than one feature answers to the same trigger word, so every one of them runs while the word is stripped once: {signatureToLog}", LogLevel.Warn);
    }
}
