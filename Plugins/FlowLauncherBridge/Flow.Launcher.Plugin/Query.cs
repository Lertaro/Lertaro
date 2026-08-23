using System.Text.Json.Serialization;

namespace Flow.Launcher.Plugin;

/// <summary>
/// Represents a query that is sent to a plugin.
/// </summary>
public class Query
{
    public string OriginalQuery { get; set; } = string.Empty;

    public string RawQuery
    {
        get => TrimmedQuery;
        set => TrimmedQuery = value;
    }

    public string TrimmedQuery { get; set; } = string.Empty;
    public bool IsReQuery { get; set; }
    public bool IsHomeQuery { get; set; }
    public string Search { get; set; } = string.Empty;
    public string[] SearchTerms { get; set; } = [];

    public const string TermSeparator = " ";
    public const string ActionKeywordSeparator = TermSeparator;
    public const string GlobalPluginWildcardSign = "*";

    public string ActionKeyword { get; set; } = string.Empty;

    [JsonIgnore]
    public string FirstSearch => SplitSearch(0);

    [JsonIgnore]
    private string? _secondToEndSearch;

    [JsonIgnore]
    public string SecondToEndSearch => SearchTerms.Length > 1 ? (_secondToEndSearch ??= string.Join(' ', SearchTerms[1..])) : string.Empty;

    [JsonIgnore]
    public string ThirdToEndSearch => SearchTerms.Length > 2 ? string.Join(' ', SearchTerms[2..]) : string.Empty;

    [JsonIgnore]
    public string SecondSearch => SplitSearch(1);

    [JsonIgnore]
    public string ThirdSearch => SplitSearch(2);

    [JsonIgnore]
    public string FourthToEndSearch => SearchTerms.Length > 3 ? string.Join(' ', SearchTerms[3..]) : string.Empty;

    public string SplitSearch(int index)
    {
        return (SearchTerms.Length > index && index >= 0) ? SearchTerms[index] : string.Empty;
    }

    public override string ToString() => RawQuery;
}
