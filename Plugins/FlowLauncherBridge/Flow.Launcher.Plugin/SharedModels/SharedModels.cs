namespace Flow.Launcher.Plugin.SharedModels;

public enum SearchPrecisionScore
{
    Regular = 0,
    Low = 20,
    Medium = 50,
    High = 80,
    VeryHigh = 100
}

public class MatchResult
{
    public MatchResult(bool success, SearchPrecisionScore searchPrecision)
    {
        Success = success;
        SearchPrecision = searchPrecision;
        MatchData = [];
    }

    public MatchResult(bool success, SearchPrecisionScore searchPrecision, List<int> matchData, int rawScore)
    {
        Success = success;
        SearchPrecision = searchPrecision;
        MatchData = matchData ?? [];
        RawScore = rawScore;
    }

    public bool Success { get; set; }
    public int Score { get; private set; }
    private int _rawScore;

    public int RawScore
    {
        get => _rawScore;
        set
        {
            _rawScore = value;
            Score = (value >= (int)SearchPrecision) ? value : 0;
        }
    }

    public List<int> MatchData { get; set; }
    public SearchPrecisionScore SearchPrecision { get; set; }
    public bool IsSearchPrecisionScoreMet() => _rawScore >= (int)SearchPrecision;
}

public record ThemeData(string Name, string FilePath);
