using System.IO;
using Lertaro.Plugins.Calendar.Reminders;

namespace Lertaro.Plugins.Calendar.Tests.Reminders;

[TestClass]
public sealed class ReminderStoreTests
{
    private static readonly DateTime At = new(2026, 9, 30, 14, 30, 0, DateTimeKind.Unspecified);

    private string _root = string.Empty;

    [TestInitialize]
    public void CreateRoot() => _root = Directory.CreateTempSubdirectory("lertaro-calendar-store-").FullName;

    [TestCleanup]
    public void RemoveRoot()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public void Add_ThenASecondStoreReadsTheSameFile()
    {
        var first = new ReminderStore(_root);
        var added = first.Add(At, "dentist");

        var reopened = new ReminderStore(_root);
        var loaded = reopened.Snapshot();

        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual(added.Id, loaded[0].Id);
        Assert.AreEqual("dentist", loaded[0].Text);
        Assert.AreEqual(At, loaded[0].At);
        Assert.IsNull(loaded[0].FiredAt);
    }

    [TestMethod]
    public void Add_TruncatesToTheMinuteAndKeepsTheWallClockReading()
    {
        var store = new ReminderStore(_root);

        var added = store.Add(new DateTime(2026, 9, 30, 14, 30, 57, 800), "  padded  ");

        Assert.AreEqual(new DateTime(2026, 9, 30, 14, 30, 0), added.At);
        Assert.AreEqual(DateTimeKind.Unspecified, added.At.Kind);
        Assert.AreEqual("padded", added.Text, "surrounding whitespace is not part of the reminder");
    }

    [TestMethod]
    public void Remove_DropsOnlyTheNamedReminder()
    {
        var store = new ReminderStore(_root);
        var keep = store.Add(At, "keep");
        var drop = store.Add(At.AddHours(1), "drop");

        Assert.IsTrue(store.Remove(drop.Id));
        Assert.IsFalse(store.Remove("no such id"));
        CollectionAssert.AreEqual(new[] { keep.Id }, store.Snapshot().Select(r => r.Id).ToList());
    }

    [TestMethod]
    public void Apply_MarksFiredAndPrunesInOneWrite()
    {
        var store = new ReminderStore(_root);
        var fired = store.Add(At, "fired");
        var gone = store.Add(At.AddDays(-2), "expired");
        var kept = store.Add(At.AddHours(3), "future");

        store.Apply(new[] { fired.Id }, new[] { gone.Id }, At.AddMinutes(1));

        var loaded = new ReminderStore(_root).Snapshot();
        Assert.AreEqual(2, loaded.Count);
        Assert.AreEqual(kept.Id, loaded.Single(r => r.Text == "future").Id);
        Assert.AreEqual(At.AddMinutes(1), loaded.Single(r => r.Text == "fired").FiredAt);
    }

    [TestMethod]
    public void Serialize_ThenParse_RoundTripsTheWholeList()
    {
        var items = new[]
        {
            new CalendarReminder("b", At, "second", null),
            new CalendarReminder("a", At.AddHours(-1), "first", At),
        };

        var parsed = ReminderStore.Parse(ReminderStore.Serialize(items));

        CollectionAssert.AreEqual(
            new[] { "a", "b" },
            parsed.OrderBy(p => p.At).ThenBy(p => p.Id, StringComparer.Ordinal).Select(p => p.Id).ToList());
        Assert.AreEqual(At, parsed.Single(p => p.Id == "b").At);
        Assert.AreEqual(At, parsed.Single(p => p.Id == "a").FiredAt);
        Assert.AreEqual(DateTimeKind.Unspecified, parsed.Single(p => p.Id == "a").At.Kind);
    }

    [TestMethod]
    public void Parse_DropsRowsItCannotTrustRatherThanFailingTheWholeFile()
    {
        const string json = """
            {"Version":1,"Reminders":[
              {"Id":"ok","At":"2026-09-30T14:30","Text":"keep","FiredAt":null},
              {"Id":"bad","At":"30/09/2026","Text":"unparseable time","FiredAt":null},
              {"Id":"empty","At":"2026-09-30T15:00","Text":"   ","FiredAt":null},
              {"Id":"","At":"2026-09-30T16:00","Text":"no id","FiredAt":null}
            ]}
            """;

        var parsed = ReminderStore.Parse(json);

        CollectionAssert.AreEqual(new[] { "ok" }, parsed.Select(p => p.Id).ToList());
    }

    [TestMethod]
    public void AFileThatCannotBeReadIsSetAsideAndStartsEmpty()
    {
        var store = new ReminderStore(_root);
        store.Add(At, "the list before it went bad");
        var calendarFolder = Path.Combine(_root, "Calendar");
        var live = Path.Combine(calendarFolder, "reminders.json");
        File.WriteAllText(live, "{ this is not json");

        var reopened = new ReminderStore(_root);

        Assert.AreEqual(0, reopened.Snapshot().Count, "the feature has to keep working");

        // What could not be parsed is renamed aside rather than overwritten, so a user who notices can
        // still recover the list by hand; the next successful save then starts from a clean file.
        Assert.IsFalse(File.Exists(live));
        var aside = Directory.GetFiles(calendarFolder)
            .Where(f => Path.GetFileName(f)!.StartsWith("reminders.json.bad-", StringComparison.Ordinal))
            .ToList();
        Assert.AreEqual(1, aside.Count);
        Assert.AreEqual("{ this is not json", File.ReadAllText(aside[0]));

        reopened.Add(At.AddHours(1), "a fresh start");
        CollectionAssert.AreEqual(new[] { "a fresh start" }, reopened.Snapshot().Select(r => r.Text).ToList());
    }

    [TestMethod]
    public void Snapshot_LeavesNoFileBehindForAnEmptyList()
    {
        Assert.AreEqual(0, new ReminderStore(_root).Snapshot().Count);
        Assert.IsFalse(File.Exists(Path.Combine(_root, "Calendar", "reminders.json")));
    }
}
