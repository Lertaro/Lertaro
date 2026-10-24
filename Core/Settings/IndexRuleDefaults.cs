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

    /// <summary>
    /// Retired from <see cref="CreateLow"/>, and removed unconditionally from every saved list (see
    /// <see cref="IndexRuleMigration.Ensure"/>). Its regex is tested against the search-root-relative path
    /// as well as the leaf name, so an extensionless *ancestor* directory matched it --
    /// <c>D:\tools\smalltools\...</c> matched at <c>tools\smalltools</c> -- and the whole tree inherited Low.
    /// That demoted every program kept outside Program Files, which is not what "demote names without an
    /// extension" was meant to do. A named constant so the migration and this doc cannot drift.
    /// </summary>
    public const string RetiredExtensionlessRegex = @"/^[^.]+$/";

    /// <summary>
    /// System Settings rows carry the provider's CLR type name in their synthetic path
    /// (SearchableItemMapper.SyntheticPath), not the localized display name, so one rule demotes them in
    /// every UI language. Keywords are a good cross-language recall path but a poor identity claim: Windows
    /// tags several control-panel tasks with the same keyword ("vpn" is on both the network-connections and
    /// the VPN-setup task), so an exact keyword hit must not outrank direct name matches.
    /// </summary>
    public const string SystemSettingsLowPattern = @"__SEARCHABLE_ITEM__:SystemSettingsItemProvider:*";

    public static IEnumerable<IndexRuleSetting> CreateLow() => new[]
    {
        Low(SystemSettingsLowPattern),
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
        Low(@"/^\$/"),
    };

    private static IndexRuleSetting Low(string pattern) => new()
    {
        Pattern = pattern,
        Effect = IndexRuleEffect.Low,
    };
}
