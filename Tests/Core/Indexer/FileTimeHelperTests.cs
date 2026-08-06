namespace Lertaro.Core.Tests.Indexer;

[TestClass]
public sealed class FileTimeHelperTests
{
    [TestMethod]
    public void ToUnixSeconds_Epoch_ReturnsZero() => Assert.AreEqual(0u, FileTimeHelper.ToUnixSeconds(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

    [TestMethod]
    public void ToUnixSeconds_BeforeEpoch_ClampsToZero() => Assert.AreEqual(0u, FileTimeHelper.ToUnixSeconds(new DateTime(1960, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

    [TestMethod]
    public void ToUnixSeconds_KnownDate_ConvertsCorrectly()
    {
        var date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.AreEqual(1704067200u, FileTimeHelper.ToUnixSeconds(date));
    }

    [TestMethod]
    public void FromUnixSeconds_Zero_ReturnsDateTimeMinValue() =>
        // 0 is the sentinel for "not recorded" throughout the index, not a real 1970 timestamp.
        Assert.AreEqual(DateTime.MinValue, FileTimeHelper.FromUnixSeconds(0));

    [TestMethod]
    public void FromUnixSeconds_ThenToUnixSeconds_RoundTrips()
    {
        var original = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var seconds = FileTimeHelper.ToUnixSeconds(original);

        var restored = FileTimeHelper.FromUnixSeconds(seconds);

        Assert.AreEqual(original, restored);
    }

    [TestMethod]
    public void FileTimeToUnixSeconds_ZeroOrNegative_ReturnsZero()
    {
        Assert.AreEqual(0u, FileTimeHelper.FileTimeToUnixSeconds(0));
        Assert.AreEqual(0u, FileTimeHelper.FileTimeToUnixSeconds(-1));
    }

    [TestMethod]
    public void FileTimeToUnixSeconds_ValidFileTime_MatchesDirectConversion()
    {
        var date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var fileTime = date.ToFileTimeUtc();

        Assert.AreEqual(FileTimeHelper.ToUnixSeconds(date), FileTimeHelper.FileTimeToUnixSeconds(fileTime));
    }

    [TestMethod]
    public void FileTimeToUnixSeconds_OutOfRangeValue_ReturnsZeroInsteadOfThrowing() => Assert.AreEqual(0u, FileTimeHelper.FileTimeToUnixSeconds(long.MaxValue));

    [TestMethod]
    public void TryGetLastWriteTimeUnixSeconds_RealDirectory_ReturnsNonZero()
    {
        using var dir = new TempDirectory();

        var result = FileTimeHelper.TryGetLastWriteTimeUnixSeconds(dir.Path);

        Assert.AreNotEqual(0u, result);
        Assert.AreEqual(FileTimeHelper.ToUnixSeconds(Directory.GetLastWriteTimeUtc(dir.Path)), result);
    }

    [TestMethod]
    public void TryGetLastWriteTimeUnixSeconds_NonexistentPath_ReturnsZeroInsteadOfThrowing() =>
        Assert.AreEqual(0u, FileTimeHelper.TryGetLastWriteTimeUnixSeconds(@"Z:\this\path\does\not\exist\at\all"));

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
