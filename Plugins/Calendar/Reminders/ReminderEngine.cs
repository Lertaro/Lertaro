using Lertaro.PluginSdk;

namespace Lertaro.Plugins.Calendar.Reminders;

internal enum ReminderOutcome
{
    Wait,
    Fire,
    FireLate,
    Expire
}

/// <summary>
/// Watches the reminder list and hands each one to the notifier when its moment arrives.
/// </summary>
/// <remarks>
/// Level-triggered off the wall clock on a fixed poll, rather than armed with a timer per deadline. A
/// due-time timer has to be re-armed after every sleep, clock adjustment and list edit, and each of those
/// is a chance to miss one silently, which is the worst failure this feature has. Worse, both
/// <see cref="Timer"/> and <see cref="Task.Delay"/> count against the system's unbiased tick, which does
/// not advance in S3 or hibernation, so a reminder armed for Monday while the laptop closes over the
/// weekend goes off two days of *awake* time later; and a plugin has no power event to recover from, because
/// the only system events a plugin can see are the ones the host happens to subscribe to. A poll has
/// one code path, compare the data to now, and that same path is what catches up after the app was closed
/// outright: the first tick after startup is Reconcile, with no separate cold-start branch to get wrong.
/// </remarks>
internal sealed class ReminderEngine : IDisposable
{
    internal const int TickIntervalMs = 15_000;

    /// <summary>How late a missed reminder may be and still be worth announcing on catch-up.</summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromHours(24);

    /// <summary>Past this, the notification says it is late rather than pretending to be on time.</summary>
    internal static readonly TimeSpan LateAfter = TimeSpan.FromMinutes(20);

    private readonly ReminderStore _store;
    private readonly Action<CalendarReminder, bool> _present;
    private readonly object _lifecycle = new();

    private CancellationTokenSource? _cts;
    private Task? _worker;
    private DateTime _lastTick = DateTime.MinValue;

    internal ReminderEngine(ReminderStore store, Action<CalendarReminder, bool> present)
    {
        _store = store;
        _present = present;
    }

    internal static ReminderOutcome Decide(
        DateTime at, DateTime? firedAt, DateTime now, TimeSpan grace, TimeSpan lateAfter)
    {
        // Already delivered: it stays in the list only until the next tick, which is what "deleted once
        // it has gone off" means here.
        if (firedAt != null) return ReminderOutcome.Expire;
        if (now < at) return ReminderOutcome.Wait;

        var late = now - at;
        if (late > grace) return ReminderOutcome.Expire;
        return late > lateAfter ? ReminderOutcome.FireLate : ReminderOutcome.Fire;
    }

    /// <summary>
    /// One pass of the whole list: drop what is gone past, deliver everything that is due, and report how many
    /// were delivered. Shared by every tick, the first tick after startup, an edit made in the view, and
    /// the settings page's test button, so there is no second catch-up implementation to drift.
    /// </summary>
    internal int Reconcile(DateTime now)
    {
        var removed = new List<string>();
        var due = new List<(CalendarReminder Reminder, bool Late)>();

        foreach (var reminder in _store.Snapshot())
        {
            switch (Decide(reminder.At, reminder.FiredAt, now, Grace, LateAfter))
            {
                case ReminderOutcome.Expire:
                    removed.Add(reminder.Id);
                    break;
                case ReminderOutcome.Fire:
                    due.Add((reminder, false));
                    break;
                case ReminderOutcome.FireLate:
                    due.Add((reminder, true));
                    break;
            }
        }

        var batch = due
            .OrderBy(d => d.Reminder.At)
            .ThenBy(d => d.Reminder.Id, StringComparer.Ordinal)
            .ToList();

        if (batch.Count > 0 || removed.Count > 0)
        {
            // Persisted before the notifications are handed over, so delivery is at-most-once: a crash in
            // between costs those reminders rather than re-announcing them on every start from now on.
            _store.Apply(batch.Select(d => d.Reminder.Id).ToList(), removed, now);
        }

        // All of them, oldest first: a backlog reads as "here is what you missed", and the host stacks five
        // cards with five more queued behind them per plugin. What is past even that is refused by the host,
        // which logs each one it drops -- the designed overflow, not a silence from here.
        foreach (var (reminder, late) in batch)
            _present(reminder, late);

        NoteTickGap(now);
        _lastTick = now;
        return batch.Count;
    }

    /// <summary>Asks for a pass off the calling thread; used by the view and the settings test button.</summary>
    internal void ReconcileSoon()
    {
        var worker = _worker;
        if (worker == null) return;
        _ = Task.Run(() =>
        {
            try { Reconcile(DateTime.Now); }
            catch (Exception ex) { Logger.Log($"[Calendar] catch-up pass failed: {ex.Message}", LogLevel.Error); }
        });
    }

    internal void Start()
    {
        lock (_lifecycle)
        {
            if (_worker != null) return;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _worker = Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        // Its own below-normal thread, so the poll never wins CPU against the UI and stays
                        // out of the thread pool, where its priority could be starved by other work.
                        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                        Loop(token);
                    }
                    catch (OperationCanceledException) { }
                },
                token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
    }

    internal void Stop()
    {
        lock (_lifecycle)
        {
            _cts?.Cancel();
            try { _worker?.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            catch { }

            _worker = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Loop(CancellationToken token)
    {
        while (true)
        {
            // A tick must never take the loop down with it: one bad read would otherwise cost every
            // remaining reminder for the rest of the session, silently.
            try { Reconcile(DateTime.Now); }
            catch (Exception ex) { Logger.Log($"[Calendar] reminder tick failed and is retried next tick: {ex.Message}", LogLevel.Error); }

            if (token.WaitHandle.WaitOne(TickIntervalMs)) return;
        }
    }

    private void NoteTickGap(DateTime now)
    {
        if (_lastTick == DateTime.MinValue) return;

        var gap = now - _lastTick;
        if (gap > TimeSpan.FromMilliseconds(TickIntervalMs * 2) || gap < TimeSpan.Zero)
        {
            // The only clue that separates "the loop died" from "the machine slept", and the reason a
            // reminder went out tagged late rather than on time. Nothing branches on it.
            Logger.Log($"[Calendar] reconciled a {gap.TotalMinutes:F1} minute gap in reminder timing", LogLevel.Info);
        }
    }

    public void Dispose() => Stop();
}
