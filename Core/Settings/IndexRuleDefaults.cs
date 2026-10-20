namespace Lertaro.Core;

/// <summary>The shipped rule set, and the legacy lists used to recognise an untouched old configuration.</summary>
public static class IndexRuleDefaults
{
    public static readonly string[] LegacyExcludedPaths =
    {
        @"%SystemDrive%\Windows.old",
        @"%ProgramData%",
        @"%SystemRoot%",
        @"%ProgramW6432%",
        @"%USERPROFILE%\AppData",
        @"%ProgramFiles(x86)%",
    };

    public static readonly string[] LegacyIgnoredGlobs =
    {
        ".*",
        "~*",
        @"\$*",
        "node_modules",
    };

    /// <summary>1 exclusion plus the 15 low-priority defaults; the app directories are demoted, not hidden.</summary>
    public static List<IndexRuleSetting> Create()
    {
        var rules = new List<IndexRuleSetting>
        {
            new() { Pattern = "node_modules", Effect = IndexRuleEffect.Exclude },
        };
        rules.AddRange(CreateLow());
        return rules;
    }

    public static IEnumerable<IndexRuleSetting> CreateLow() => new[]
    {
        Low(@"%SystemDrive%\Windows.old"),
        Low(@"%SystemDrive%\$WINDOWS.~BT"),
        Low(@"%SystemDrive%\$Windows.~WS"),
        Low(@"%ProgramData%"),
        Low(@"%SystemRoot%"),
        Low(@"%USERPROFILE%\AppData"),
        Low(@"%ProgramW6432%"),
        Low(@"%ProgramFiles(x86)%"),
        Low(@"build\"),
        Low(@"debug\"),
        Low(@"target\"),
        Low(@"/^\./"),
        Low(@"/^~/"),
        Low(@"/^[^.]+$/"),
        Low(@"/^\$/"),
    };

    private static IndexRuleSetting Low(string pattern) => new()
    {
        Pattern = pattern,
        Effect = IndexRuleEffect.Low,
    };
}
