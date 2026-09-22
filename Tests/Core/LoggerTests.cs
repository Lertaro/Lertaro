namespace Lertaro.Core.Tests;

// Logger is a static class holding repeat-suppression state and a temp log file path;
// these tests must not run concurrently with anything else touching that state.
[TestClass]
[DoNotParallelize]
public sealed class LoggerTests
{
    private string _baseDir = null!;
    private string _logPath = null!;

    [TestInitialize]
    public void SetUp()
    {
        // Logger.Initialize appends a "logs" segment to the base directory itself.
        _baseDir = Path.Combine(Path.GetTempPath(), "TestLogger_" + Guid.NewGuid().ToString("N"));
        _logPath = Path.Combine(_baseDir, "logs", "test.log");
        Logger.Initialize("test.log", baseDirectory: _baseDir, overwrite: true);
    }

    [TestCleanup]
    public void TearDown()
    {
        // Logger holds the log file open for the process lifetime, so this test's directory cannot be
        // deleted while it is the current log. Hand the handle to a throwaway path first.
        Logger.Initialize("release.log", baseDirectory: Path.Combine(Path.GetTempPath(), "LertaroLoggerRelease"));
        try { Directory.Delete(_baseDir, recursive: true); } catch { }
    }

    private IReadOnlyList<string> Lines() => Logger.ReadLogLines(_logPath);

    [TestMethod]
    public void Log_DistinctMessages_EachWrittenOnce()
    {
        Logger.Log("first", LogLevel.Warn);
        Logger.Log("second", LogLevel.Info);
        Logger.Log("third", LogLevel.Warn);

        var lines = Lines();
        Assert.HasCount(4, lines); // plus the "Log initialized" line
        Assert.Contains("first", lines[1]);
        Assert.Contains("second", lines[2]);
        Assert.Contains("third", lines[3]);
    }

    [TestMethod]
    public void Log_ConsecutiveRepeats_CondensedToTallyLines()
    {
        for (var i = 0; i < 10; i++)
            Logger.Log("same warning", LogLevel.Warn);
        Logger.Log("a different message", LogLevel.Info);

        var lines = Lines();
        var warningLines = lines.Where(l => l.Contains("same warning", StringComparison.Ordinal)).ToList();
        Assert.HasCount(2, warningLines); // first occurrence + the x10 tally line
        Assert.Contains("(repeated x10)", warningLines[1]);
    }

    [TestMethod]
    public void Log_RepeatRunEndedBetweenReportPoints_FlushesFinalTally()
    {
        Logger.Log("spiky message", LogLevel.Warn);
        for (var i = 0; i < 4; i++)
            Logger.Log("spiky message", LogLevel.Warn); // 5 occurrences total, no x10 reached
        Logger.Log("next message", LogLevel.Info);

        var lines = Lines();
        var tallyLines = lines.Where(l => l.Contains("(repeated x5)", StringComparison.Ordinal)).ToList();
        Assert.HasCount(1, tallyLines);
    }

    [TestMethod]
    public void Log_SingleMessage_FlushesNoTally()
    {
        Logger.Log("once only", LogLevel.Warn);
        Logger.Log("another", LogLevel.Info);

        var lines = Lines();
        Assert.HasCount(3, lines);
        Assert.IsEmpty(lines.Where(l => l.Contains("(repeated x", StringComparison.Ordinal)).ToList());
    }

    [TestMethod]
    public void Log_SameTextDifferentLevel_NotCondensed()
    {
        Logger.Log("level switch", LogLevel.Info);
        Logger.Log("level switch", LogLevel.Warn);
        Logger.Log("level switch", LogLevel.Info);

        var lines = Lines();
        Assert.HasCount(4, lines); // each level's first occurrence is written in full
    }

    [TestMethod]
    public void ClearCurrentLog_ResetsRepeatState()
    {
        Logger.Log("pre-clear message", LogLevel.Warn);
        Logger.ClearCurrentLog();
        Logger.Log("pre-clear message", LogLevel.Warn);

        var lines = Lines();
        Assert.HasCount(2, lines); // "Log cleared" + the message as a fresh first occurrence
        Assert.IsEmpty(lines.Where(l => l.Contains("(repeated x", StringComparison.Ordinal)).ToList());
    }

    [TestMethod]
    public void Initialize_Overwrite_StartsFreshLog()
    {
        Logger.Log("previous session", LogLevel.Warn);

        Logger.Initialize("test.log", baseDirectory: _baseDir, overwrite: true);

        var lines = Lines();
        Assert.HasCount(1, lines);
        Assert.Contains("Log initialized", lines[0]);
    }

    [TestMethod]
    public void Initialize_AppendUnderSizeCap_KeepsPreviousRun()
    {
        Logger.Log("previous session", LogLevel.Warn);

        Logger.Initialize("test.log", baseDirectory: _baseDir, overwrite: false);

        var lines = Lines();
        Assert.HasCount(3, lines); // init line + the previous run's message + the resume marker
        Assert.Contains("previous session", lines[1]);
        Assert.Contains("Log resumed", lines[2]);
    }

    [TestMethod]
    public void Initialize_AppendPastSizeCap_StartsFreshLog()
    {
        AppendRaw(new string('x', 1024 * 1024));

        Logger.Initialize("test.log", baseDirectory: _baseDir, overwrite: false);

        var lines = Lines();
        Assert.HasCount(1, lines);
        Assert.Contains("Log initialized", lines[0]);
    }

    [TestMethod]
    public void WriteLine_PastSizeCap_RollsTheLogOver()
    {
        var chunk = new string('x', 4000);
        for (var i = 0; i < 400; i++) // ~1.6 MB of distinct lines, so the cap is crossed mid-run
            Logger.Log($"{chunk} line {i}", LogLevel.Warn);

        // Assert.IsLessThan takes (upperBound, value).
        Assert.IsLessThan(1024L * 1024L, new FileInfo(_logPath).Length,
            "a single long run must not grow the log past the cap");
        Assert.Contains("Log rolled over", string.Join('\n', Lines()));
    }

    /// <summary>
    /// Writes around the open log handle: Logger keeps one writer on the file for the process lifetime,
    /// so a plain File.AppendAllText here would be a sharing violation.
    /// </summary>
    private void AppendRaw(string content)
    {
        using var stream = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }
}
