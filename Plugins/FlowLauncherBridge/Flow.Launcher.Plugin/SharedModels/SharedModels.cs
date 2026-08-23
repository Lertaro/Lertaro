using System.Windows;

namespace Flow.Launcher.Plugin.SharedModels;

public enum SearchPrecisionScore
{
    None = 0,
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

public class ThemeData
{
    public string FileNameWithoutExtension { get; private init; }
    public string Name { get; private init; }
    public bool? IsDark { get; private init; }
    public bool? HasBlur { get; private init; }

    public ThemeData(string fileNameWithoutExtension, string name, bool? isDark = null, bool? hasBlur = null)
    {
        FileNameWithoutExtension = fileNameWithoutExtension;
        Name = name;
        IsDark = isDark;
        HasBlur = hasBlur;
    }

    public static bool operator ==(ThemeData? left, ThemeData? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        return left.Equals(right);
    }

    public static bool operator !=(ThemeData? left, ThemeData? right) => !(left == right);

    public override bool Equals(object? obj) => obj is ThemeData other && FileNameWithoutExtension == other.FileNameWithoutExtension;
    public override int GetHashCode() => FileNameWithoutExtension?.GetHashCode() ?? 0;
    public override string ToString() => Name;
}

public class MonitorInfo
{
    public Rect Bounds { get; }
    public Rect WorkingArea { get; }
    public string Name { get; } = string.Empty;
    public bool IsPrimary { get; }

    public MonitorInfo(Rect bounds, Rect workingArea, bool isPrimary = false, string name = "")
    {
        Bounds = bounds;
        WorkingArea = workingArea;
        IsPrimary = isPrimary;
        Name = name;
    }

    public static IList<MonitorInfo> GetDisplayMonitors()
    {
        return [new MonitorInfo(SystemParameters.WorkArea, SystemParameters.WorkArea, true, "Primary")];
    }

    public static MonitorInfo GetNearestDisplayMonitor(nint hwnd)
    {
        return new MonitorInfo(SystemParameters.WorkArea, SystemParameters.WorkArea, true, "Primary");
    }

    public static MonitorInfo GetPrimaryDisplayMonitor()
    {
        return new MonitorInfo(SystemParameters.WorkArea, SystemParameters.WorkArea, true, "Primary");
    }

    public static MonitorInfo GetCursorDisplayMonitor()
    {
        return new MonitorInfo(SystemParameters.WorkArea, SystemParameters.WorkArea, true, "Primary");
    }

    public override string ToString() => Name;
}
