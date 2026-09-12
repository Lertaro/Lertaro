namespace Lertaro.Core;

// Keep the two history stores consistent: disabling a store blocks new entries, but an existing
// entry may still accumulate usage so its count remains meaningful for cleanup and ranking.
internal static class HistoryRecordingPolicy
{
    public static bool ShouldRecord(bool enabled, bool entryExists) => enabled || entryExists;
}
