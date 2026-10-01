using System.Globalization;
using System.IO;
using System.Text.Json;
using Lertaro.PluginSdk;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.Calendar.Reminders;

/// <param name="At">Local wall-clock time, truncated to the minute, Kind Unspecified.</param>
/// <param name="FiredAt">Set the moment the reminder is handed to the notifier, and the reason it is
/// deleted on the next tick rather than re-shown. Never re-notified, which is what keeps a crash
/// between "record it" and "show it" from turning one reminder into a daily one.</param>
internal sealed record CalendarReminder(string Id, DateTime At, string Text, DateTime? FiredAt)
{
    internal const string TimeFormat = "yyyy-MM-ddTHH:mm";

    internal string AtText => At.ToString(TimeFormat, CultureInfo.InvariantCulture);
}

/// <summary>
/// The reminder list, held in one JSON file under the plugin's own data folder.
/// </summary>
/// <remarks>
/// Not <see cref="PluginSettingsService"/>: that routes through the launcher's whole settings file, which
/// a save rewrites in full and a change broadcasts back to every listener, this plugin's own included.
/// Reminders are records the user edits several times a day, not settings, and the blast radius of a
/// half-written user-settings.json is every feature in the app rather than one calendar.
/// </remarks>
internal sealed class ReminderStore
{
    private const int QuarantineKeep = 5;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _gate = new();
    private List<CalendarReminder>? _items;

    internal ReminderStore(string? dataDirectory)
    {
        var folder = string.IsNullOrEmpty(dataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lertaro", "Calendar")
            : Path.Combine(dataDirectory, "Calendar");
        _path = Path.Combine(folder, "reminders.json");
    }

    internal ReminderStore()
        : this(UserDataService.GetUserDataDirectory())
    {
    }

    internal string FilePath => _path;

    internal IReadOnlyList<CalendarReminder> Snapshot()
    {
        lock (_gate)
        {
            var items = EnsureLoaded();
            return items.ToList();
        }
    }

    internal CalendarReminder Add(DateTime at, string text)
    {
        var reminder = new CalendarReminder(
            Guid.NewGuid().ToString("N"),
            new DateTime(at.Year, at.Month, at.Day, at.Hour, at.Minute, 0, DateTimeKind.Unspecified),
            text.Trim(),
            null);

        lock (_gate)
        {
            var items = EnsureLoaded();
            items.Add(reminder);
            Save(items);
        }
        return reminder;
    }

    internal bool Remove(string id)
    {
        lock (_gate)
        {
            var items = EnsureLoaded();
            if (items.RemoveAll(r => r.Id == id) == 0) return false;
            Save(items);
            return true;
        }
    }

    /// <summary>Records delivery and prunes everything already delivered or past its grace, in one write.</summary>
    internal void Apply(IReadOnlyCollection<string> firedIds, IReadOnlyCollection<string> removedIds, DateTime now)
    {
        if (firedIds.Count == 0 && removedIds.Count == 0) return;

        lock (_gate)
        {
            var items = EnsureLoaded();
            var fired = firedIds.ToHashSet(StringComparer.Ordinal);
            var gone = removedIds.ToHashSet(StringComparer.Ordinal);

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (fired.Contains(item.Id))
                    items[i] = item with { FiredAt = now };
            }
            items.RemoveAll(r => gone.Contains(r.Id));
            Save(items);
        }
    }

    private List<CalendarReminder> EnsureLoaded()
    {
        if (_items != null) return _items;

        if (!File.Exists(_path))
        {
            _items = new List<CalendarReminder>();
            return _items;
        }

        try
        {
            _items = Parse(File.ReadAllText(_path));
        }
        catch (Exception ex)
        {
            // A list that cannot be read is renamed aside and restarted rather than thrown over: the
            // alternative is a calendar whose reminder pane is permanently dead, and the user cannot
            // tell that from having never set anything.
            _items = new List<CalendarReminder>();
            Quarantine(ex);
        }
        return _items;
    }

    private void Quarantine(Exception cause)
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(_path, $"{_path}.bad-{stamp}", overwrite: true);
            Logger.Log($"[Calendar] reminders.json unreadable and set aside: {cause.Message}", LogLevel.Error);

            // The stamp sorts lexicographically, so the surplus to drop is simply the oldest few.
            var folder = Path.GetDirectoryName(_path) ?? string.Empty;
            var pattern = Path.GetFileName(_path) + ".bad-*";
            var copies = Directory.GetFiles(folder, pattern).OrderBy(p => p, StringComparer.Ordinal).ToList();
            foreach (var stale in copies.Take(Math.Max(0, copies.Count - QuarantineKeep)))
                File.Delete(stale);
        }
        catch (Exception move)
        {
            Logger.Log($"[Calendar] reminders.json unreadable and could not be set aside: {move.Message}", LogLevel.Error);
        }
    }

    private void Save(List<CalendarReminder> items)
    {
        var folder = Path.GetDirectoryName(_path) ?? string.Empty;
        Directory.CreateDirectory(folder);

        var json = Serialize(items);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _path, overwrite: true);
    }

    internal static string Serialize(IReadOnlyList<CalendarReminder> items)
    {
        var file = new ReminderFile
        {
            Reminders = items
                .OrderBy(r => r.At)
                .ThenBy(r => r.Id, StringComparer.Ordinal)
                .Select(r => new ReminderRow
                {
                    Id = r.Id,
                    At = r.AtText,
                    Text = r.Text,
                    FiredAt = r.FiredAt?.ToString(CalendarReminder.TimeFormat, CultureInfo.InvariantCulture)
                })
                .ToList()
        };
        return JsonSerializer.Serialize(file, JsonOptions);
    }

    internal static List<CalendarReminder> Parse(string json)
    {
        var file = JsonSerializer.Deserialize<ReminderFile>(json)
                   ?? throw new InvalidDataException("reminders.json is not an object");

        var items = new List<CalendarReminder>(file.Reminders.Count);
        foreach (var row in file.Reminders)
        {
            if (string.IsNullOrWhiteSpace(row.Id) || string.IsNullOrWhiteSpace(row.Text)) continue;
            if (!DateTime.TryParseExact(
                    row.At, CalendarReminder.TimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var at))
                continue;

            DateTime? fired = null;
            if (!string.IsNullOrWhiteSpace(row.FiredAt) &&
                DateTime.TryParseExact(row.FiredAt, CalendarReminder.TimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var f))
                fired = f;

            items.Add(new CalendarReminder(row.Id, at, row.Text, fired));
        }
        return items;
    }

    private sealed class ReminderFile
    {
        public int Version { get; set; } = 1;
        public List<ReminderRow> Reminders { get; set; } = new();
    }

    private sealed class ReminderRow
    {
        public string Id { get; set; } = string.Empty;
        public string At { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string? FiredAt { get; set; }
    }
}
