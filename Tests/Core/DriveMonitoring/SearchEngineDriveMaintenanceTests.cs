using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.Tests.DriveMonitoring;

// A re-index request raised while the same drive's own rebuild is still running -- the missed-folder-change
// follow-up at the end of that rebuild, or a monitor's request -- used to be refused as a duplicate and lost.
// Most rebuilds here run against an unmounted letter: such a rebuild fails fast and parks the drive without
// touching a volume, which is enough because follow-ups are decided after a rebuild whatever its outcome.
// Tests that need a mounted drive (a volume id, a status refresh) cancel every rebuild of it before it runs,
// so none of them reads the volume. The test runs each queued rebuild itself, and delayed retries wait on a
// clock the test advances. The lifetime source is cancelled but never disposed: a parked drive's re-insertion
// waiter is still registered on it (SearchEngine cancels without disposing for the same reason).
[TestClass]
public sealed class SearchEngineDriveMaintenanceTests
{
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(30);

    private static string FreeDriveLetter() =>
        Enumerable.Range('A', 26).Select(c => ((char)c).ToString()).First(l => !VolumeHelper.DetectIndexableLocalDrives().Contains(l));

    private sealed class Rig : IDisposable
    {
        public readonly CancellationTokenSource Lifetime = new();
        public readonly UsnIndexer Indexer = new();
        public readonly ManualTime Time = new();
        public readonly WorkQueue Work = new();
        public readonly SearchEngineDriveMaintenance Maintenance;

        public Rig(Func<MachineSettings>? settings = null, Func<bool>? isRebuilding = null, Func<CancellationToken>? token = null) =>
            Maintenance = new SearchEngineDriveMaintenance(Indexer, settings ?? (() => new MachineSettings()),
                token ?? (() => Lifetime.Token), isRebuilding ?? (() => false), () => { }, Time, Work.Add);

        public void Dispose()
        {
            Lifetime.Cancel();
            Indexer.Dispose();
        }
    }

    // Rebuild work, run only when the test says so. Locked, because a delayed retry queues its rebuild from the
    // pool thread its timer continuation runs on.
    private sealed class WorkQueue
    {
        private readonly List<Action> _items = new();

        public void Add(Action work)
        {
            lock (_items)
            {
                _items.Add(work);
                Monitor.PulseAll(_items);
            }
        }

        public int Count { get { lock (_items) return _items.Count; } }

        public void Run(int index)
        {
            Action work;
            lock (_items)
                work = _items[index];
            work();
        }

        public void WaitForCount(int count)
        {
            lock (_items)
                while (_items.Count < count)
                    if (!Monitor.Wait(_items, TimeSpan.FromSeconds(10)))
                        Assert.Fail($"Expected {count} queued rebuilds, got {_items.Count}.");
        }
    }

    // A clock that only moves when the test advances it. Task.Delay(delay, TimeProvider, token) waits on one
    // timer from CreateTimer and disposes it when the token is cancelled.
    private sealed class ManualTime : TimeProvider
    {
        private readonly List<ManualTimer> _timers = new();
        private TimeSpan _now;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_timers)
                return DateTimeOffset.UnixEpoch + _now;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            List<ManualTimer> due;
            lock (_timers)
            {
                _now += by;
                due = _timers.Where(t => t.DueAt <= _now).ToList();
                _timers.RemoveAll(due.Contains);
            }
            foreach (var timer in due)
                timer.Fire();
        }

        private sealed class ManualTimer(ManualTime time, TimerCallback callback, object? state) : ITimer
        {
            public TimeSpan DueAt { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (time._timers)
                {
                    time._timers.Remove(this);
                    if (dueTime == Timeout.InfiniteTimeSpan)
                        return true;
                    DueAt = time._now + dueTime;
                    time._timers.Add(this);
                }
                return true;
            }

            public void Fire() => callback(state);

            public void Dispose()
            {
                lock (time._timers)
                    time._timers.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    // The original rebuild and its immediate rerun, each asked to re-index again while it ran: the drive is
    // left waiting on a delayed retry, with two rebuilds queued and run.
    private static void ReachWaitingRetry(Rig rig, string drive)
    {
        Assert.IsTrue(rig.Maintenance.QueueDriveRebuild(drive, forceRebuild: false));
        rig.Maintenance.QueueDriveRebuild(drive);
        rig.Work.Run(0);
        rig.Maintenance.QueueDriveRebuild(drive);
        rig.Work.Run(1);
        Assert.AreEqual(2, rig.Work.Count, "precondition: the follow-up of a follow-up does not run at once");
        Assert.IsFalse(rig.Maintenance.HasPendingRebuilds);
    }

    private static MachineSettings Selecting(string drive) => new() { LocalDrives = { VolumeHelper.GetVolumeId(drive)! } };

    // ReachWaitingRetry for a mounted drive: its one walk is stopped before it runs, and the request it was
    // asked for meanwhile survives the Stop as a waiting retry.
    private static void ReachWaitingRetryWithoutReadingTheVolume(Rig rig, string drive)
    {
        Assert.IsTrue(rig.Maintenance.QueueDriveRebuild(drive, forceRebuild: false));
        Assert.IsTrue(rig.Maintenance.CancelDriveRebuild(drive));
        rig.Maintenance.QueueDriveRebuild(drive);
        rig.Work.Run(0);
        Assert.AreEqual(1, rig.Work.Count, "precondition: a follow-up of a stopped walk does not run at once");
        Assert.IsFalse(rig.Maintenance.HasPendingRebuilds);
    }

    // True when a plain request collapses into a waiting retry instead of starting a rebuild. Starts one when
    // it does not, so it is only ever the last check of a test.
    private static bool IsWaiting(Rig rig, string drive)
    {
        var before = rig.Work.Count;
        rig.Maintenance.QueueDriveRebuild(drive);
        return rig.Work.Count == before;
    }

    [TestMethod]
    public void QueueDriveRebuild_RequestedWhileThatDriveRebuilds_RunsOnceAfterIt()
    {
        using var rig = new Rig();
        var drive = FreeDriveLetter();

        Assert.IsTrue(rig.Maintenance.QueueDriveRebuild(drive, forceRebuild: true));
        // Two requests during one rebuild collapse into one rerun.
        rig.Maintenance.QueueDriveRebuild(drive);
        rig.Maintenance.QueueDriveRebuild(drive);
        rig.Work.Run(0);

        Assert.AreEqual(2, rig.Work.Count);
        Assert.IsTrue(rig.Maintenance.HasPendingRebuilds);
        rig.Lifetime.Cancel();
        rig.Work.Run(1);
        Assert.AreEqual(2, rig.Work.Count);
        Assert.IsFalse(rig.Maintenance.HasPendingRebuilds);
    }

    // A busy FAT32/exFAT drive's old folder watcher flags a missed change on every walk, so every rebuild asks
    // for another. No request may be lost, and the walk rate still has to be bounded. So the first follow-up
    // runs at once, and each one after it waits on the clock: 30 s, then doubling up to 10 min.
    [TestMethod]
    public void QueueDriveRebuild_EveryRebuildAsksForAnother_BacksOffWithoutDroppingARequest()
    {
        using var rig = new Rig();
        var drive = FreeDriveLetter();

        Assert.IsTrue(rig.Maintenance.QueueDriveRebuild(drive, forceRebuild: false));
        rig.Maintenance.QueueDriveRebuild(drive);
        rig.Work.Run(0);
        Assert.AreEqual(2, rig.Work.Count, "the first follow-up runs at once");
        var walk = 1;
        foreach (var delay in new[] { 30, 60, 120, 240, 480, 600, 600 }.Select(s => TimeSpan.FromSeconds(s)))
        {
            rig.Maintenance.QueueDriveRebuild(drive);
            rig.Work.Run(walk);
            Assert.AreEqual(walk + 1, rig.Work.Count, "a follow-up of a follow-up ran at once");
            rig.Time.Advance(delay - TimeSpan.FromMilliseconds(1));
            Assert.AreEqual(walk + 1, rig.Work.Count, $"the retry due after {delay} ran early");
            rig.Time.Advance(TimeSpan.FromMilliseconds(1));
            rig.Work.WaitForCount(walk + 2);
            walk++;
        }
    }

    // A real loss (a watcher overflow, say) during the rerun used to be logged as dropped, with nothing queued,
    // recorded or retried afterwards.
    [TestMethod]
    public void QueueDriveRebuild_RealRequestDuringTheRerun_IsNotLost()
    {
        using var rig = new Rig();
        var drive = FreeDriveLetter();

        ReachWaitingRetry(rig, drive);
        rig.Time.Advance(FirstRetryDelay);

        rig.Work.WaitForCount(3);
    }

    // The chain ends once a rebuild completes with no request raised during it, but only resets after the
    // drive has then stayed quiet for that rebuild's wait. After that, its next request runs at once again.
    [TestMethod]
    public void QueueDriveRebuild_QuietForTheWholeWaitAfterTheChainEnded_NextRequestRunsAtOnce()
    {
        using var rig = new Rig();
        var drive = FreeDriveLetter();

        ReachWaitingRetry(rig, drive);
        rig.Time.Advance(FirstRetryDelay);
        rig.Work.WaitForCount(3);
        rig.Work.Run(2);
        Assert.IsFalse(rig.Maintenance.HasPendingRebuilds, "precondition: the chain ended");
        rig.Time.Advance(FirstRetryDelay * 2); // the wait of the retry that just ran

        rig.Maintenance.QueueDriveRebuild(drive);
        rig.Maintenance.QueueDriveRebuild(drive);
        rig.Work.Run(3);

        Assert.AreEqual(5, rig.Work.Count);
    }

    // A FAT32/exFAT drive's walk recording absorbs what its live monitor asks for during the walk, so a drive
    // whose monitor overflows right after every hand-over asks only once each rebuild has ended -- and used to
    // restart an immediate whole-drive walk every time. Asked again within the last rebuild's wait, it waits,
    // and the wait keeps doubling.
    [TestMethod]
    public void QueueDriveRebuild_AskedAgainSoonAfterItsRebuildEnded_WaitsAndKeepsBackingOff()
    {
        using var rig = new Rig();
        var drive = FreeDriveLetter();

        rig.Maintenance.QueueDriveRebuild(drive);
        rig.Work.Run(0); // ends quietly
        rig.Time.Advance(FirstRetryDelay - TimeSpan.FromSeconds(1));
        rig.Maintenance.QueueDriveRebuild(drive); // the monitor overflows right after the hand-over
        Assert.AreEqual(1, rig.Work.Count, "asked again within the last rebuild's wait, and walked at once");
        rig.Time.Advance(FirstRetryDelay);
        rig.Work.WaitForCount(2);
        rig.Work.Run(1); // ends quietly too
        rig.Time.Advance(FirstRetryDelay * 2 - TimeSpan.FromSeconds(1));
        rig.Maintenance.QueueDriveRebuild(drive);
        rig.Time.Advance(FirstRetryDelay * 2 - TimeSpan.FromMilliseconds(1));
        Assert.AreEqual(2, rig.Work.Count, "the second wait did not double");
        rig.Time.Advance(TimeSpan.FromMilliseconds(1));
        rig.Work.WaitForCount(3);
    }

    // A forced rebuild (a manual one, or a failed removal's recovery) runs now and supersedes the waiting
    // retry, which then never starts a second rebuild of the same drive.
    [TestMethod]
    public void QueueDriveRebuild_ForcedWhileARetryWaits_SupersedesIt()
    {
        using var rig = new Rig();
        var drive = FreeDriveLetter();
        ReachWaitingRetry(rig, drive);

        Assert.IsTrue(rig.Maintenance.QueueDriveRebuild(drive, forceRebuild: true));
        rig.Time.Advance(FirstRetryDelay);
        rig.Work.Run(2);
        rig.Time.Advance(FirstRetryDelay); // past the forced rebuild's own wait

        Assert.AreEqual(3, rig.Work.Count);
        Assert.IsFalse(IsWaiting(rig, drive));
    }

    // Monitor and recovery requests during the wait collapse into the retry: one walk answers them all, when
    // the delay elapses.
    [TestMethod]
    public void QueueDriveRebuild_PlainRequestsWhileARetryWaits_RunOnceWhenTheDelayElapses()
    {
        using var rig = new Rig();
        var drive = FreeDriveLetter();
        ReachWaitingRetry(rig, drive);

        rig.Maintenance.QueueDriveRebuild(drive);
        rig.Maintenance.QueueDriveRebuild(drive);
        Assert.AreEqual(2, rig.Work.Count);
        Assert.IsFalse(rig.Maintenance.HasPendingRebuilds);
        rig.Time.Advance(FirstRetryDelay);
        rig.Work.WaitForCount(3);
        rig.Work.Run(2);

        Assert.AreEqual(3, rig.Work.Count);
    }

    [TestMethod]
    public void CancelDriveRebuild_WhileARetryWaits_CancelsIt()
    {
        using var rig = new Rig();
        var drive = FreeDriveLetter();
        ReachWaitingRetry(rig, drive);

        Assert.IsTrue(rig.Maintenance.CancelDriveRebuild(drive));
        rig.Time.Advance(FirstRetryDelay);

        Assert.AreEqual(2, rig.Work.Count);
        Assert.IsFalse(IsWaiting(rig, drive));
    }

    // The engine's lifetime token is cancelled by a full rebuild (which covers the drive) and by Dispose.
    [TestMethod]
    public void LifetimeCancelled_WhileARetryWaits_RetryNeverRuns()
    {
        using var rig = new Rig();
        var drive = FreeDriveLetter();
        ReachWaitingRetry(rig, drive);

        rig.Lifetime.Cancel();
        rig.Time.Advance(FirstRetryDelay);

        Assert.AreEqual(2, rig.Work.Count);
        Assert.IsFalse(IsWaiting(rig, drive));
    }

    // A query-remove cancels the drive's rebuild through the same CancelDriveRebuild a Stop uses. That
    // cancellation must not swallow the recovery a failed removal asks for afterwards.
    [TestMethod]
    public void RemovalFailsAfterItCancelledTheRebuild_ForcedRebuildStillRuns()
    {
        using var rig = new Rig();
        var drive = FreeDriveLetter();

        Assert.IsTrue(rig.Maintenance.QueueDriveRebuild(drive, forceRebuild: false));
        rig.Maintenance.QueueDriveRebuild(drive);
        Assert.IsTrue(rig.Maintenance.CancelDriveRebuild(drive)); // query-remove
        rig.Maintenance.QueueDriveRebuildAfterRemoval(drive);     // query-remove-failed
        rig.Work.Run(0);

        Assert.AreEqual(2, rig.Work.Count);
    }

    // A rebuild asked for while the drive was unplugged is replayed by its re-insertion waiter, with the
    // arguments ArmRebuildForReinsertion is handed. If the volume's arrival has already started a restore of
    // that drive, the replay used to be refused as a duplicate. Both rebuilds here are cancelled before they
    // run, so neither reads the volume.
    [TestMethod]
    public void RebuildDriveIndex_ReplayedOnReinsertionWhileTheDriveRestores_RunsForcedAfterIt()
    {
        var drive = MountedDrive();
        using var rig = new Rig(() => Selecting(drive));

        Assert.IsTrue(rig.Maintenance.QueueDriveRebuild(drive, forceRebuild: false)); // the arrival's restore
        rig.Maintenance.RebuildDriveIndex(drive, followUpIfBusy: true);               // the waiter's replay
        rig.Lifetime.Cancel();
        rig.Work.Run(0);

        Assert.AreEqual(2, rig.Work.Count, "the replayed rebuild was refused as a duplicate");
    }

    // A mounted drive removed from configuration has nothing left for its retry to recover; the
    // disabled->enabled edge restores it if it is selected again. The drive is selected again before the delay
    // elapses, so a retry that survived the refresh would show up as a collapsed request.
    [TestMethod]
    public void RefreshDrivesInStatus_DriveNoLongerEnabled_CancelsItsRetry()
    {
        var drive = MountedDrive();
        var selected = true;
        using var rig = new Rig(() => selected ? Selecting(drive) : new MachineSettings());
        ReachWaitingRetryWithoutReadingTheVolume(rig, drive);

        selected = false;
        rig.Maintenance.RefreshDrivesInStatus();
        selected = true;
        rig.Time.Advance(FirstRetryDelay);

        Assert.AreEqual(1, rig.Work.Count, "the cancelled retry rebuilt the drive");
        Assert.IsFalse(IsWaiting(rig, drive));
    }

    // A drive deselected while it rebuilt gets no follow-up, plain or forced: the refresh drops its index, and
    // selecting it again restores it.
    [TestMethod]
    public void RebuildDrive_DriveDeselectedWhileItRebuilt_GetsNoFollowUp()
    {
        var drive = MountedDrive();
        var selected = true;
        using var rig = new Rig(() => selected ? Selecting(drive) : new MachineSettings());

        Assert.IsTrue(rig.Maintenance.QueueDriveRebuild(drive, forceRebuild: false));
        Assert.IsTrue(rig.Maintenance.CancelDriveRebuild(drive)); // keep it off the volume
        rig.Maintenance.QueueDriveRebuild(drive);                 // a plain follow-up that would survive the Stop
        rig.Maintenance.QueueDriveRebuildAfterRemoval(drive);     // and a forced one
        selected = false;
        rig.Work.Run(0);

        Assert.AreEqual(1, rig.Work.Count);
        Assert.IsFalse(rig.Maintenance.HasPendingRebuilds);
    }

    // Deselected while its retry waited, with no status refresh since to cancel it: when the delay elapses the
    // retry finds the drive deselected and does not rebuild it. The retry's own continuation is what lets the
    // drive go: until it has run, a request folds into the waiting retry; once it has, the next request starts
    // the only rebuild. Had the retry rebuilt the drive, every request would fold into that rebuild instead.
    [TestMethod]
    public void DelayedRetry_DriveDeselectedWhileItWaits_DoesNotRebuildIt()
    {
        var drive = MountedDrive();
        var selected = true;
        using var rig = new Rig(() => selected ? Selecting(drive) : new MachineSettings());
        ReachWaitingRetryWithoutReadingTheVolume(rig, drive);

        selected = false;
        rig.Time.Advance(FirstRetryDelay);

        Assert.IsTrue(SpinWait.SpinUntil(() => !IsWaiting(rig, drive), TimeSpan.FromSeconds(10)),
            "the retry still holds the drive: it is waiting, or it rebuilt the deselected drive");
        Assert.AreEqual(2, rig.Work.Count, "a deselected drive was rebuilt");
    }

    // The reverse: the drive is deselected and re-selected around the moment its retry is scheduled, so the
    // deselection's refresh had nothing to cancel. The re-selection's refresh restores the drive at once
    // instead of folding into a retry that may be minutes away.
    [TestMethod]
    public void RefreshDrivesInStatus_DriveReselectedWhileARetryWaits_RestoresItAtOnce()
    {
        var drive = MountedDrive();
        var selected = true;
        using var rig = new Rig(() => selected ? Selecting(drive) : new MachineSettings());
        Assert.IsTrue(rig.Maintenance.QueueDriveRebuild(drive, forceRebuild: false));
        Assert.IsTrue(rig.Maintenance.CancelDriveRebuild(drive));
        rig.Maintenance.QueueDriveRebuild(drive);
        selected = false;
        rig.Maintenance.RefreshDrivesInStatus(); // deselected: no retry exists yet
        selected = true;
        rig.Work.Run(0); // the stopped walk ends and schedules the retry
        Assert.AreEqual(1, rig.Work.Count, "precondition: the retry is waiting");

        rig.Maintenance.RefreshDrivesInStatus(); // the re-selection's refresh

        Assert.AreEqual(2, rig.Work.Count, "the re-selected drive waited on the retry instead of being restored");
    }

    // #569's last-resort catch in FolderDriveMonitor.FlushPending asks for a re-walk when applying a change
    // throws. Raised while the drive's rebuild is queued but its walk recording not yet registered, that
    // request used to be refused as a duplicate. (Once the recording is registered, the request is left to it:
    // see FolderWalkRecorderTests.)
    [TestMethod]
    public void FolderMonitorFailsToApplyAChangeBeforeItsDrivesWalkRecordingRegisters_RerunsAfterIt()
    {
        using var rig = new Rig();
        var drive = FreeDriveLetter();
        using var asked = new ManualResetEventSlim();

        Assert.IsTrue(rig.Maintenance.QueueDriveRebuild(drive, forceRebuild: false));
        // Bound to the drive the way DriveMonitorFactory.EnsureMonitor binds it; the watcher itself never starts.
        // No walk recorder is registered for the drive, so RequestFolderReindex passes the request straight on.
        using (var monitor = new FolderDriveMonitor(Path.GetTempPath(), (_, _, _) => throw new IOException("The device is not ready."),
                   rig.Lifetime.Token, () => { rig.Indexer.RequestFolderReindex(drive, rig.Maintenance.QueueDriveRebuild); asked.Set(); }))
        {
            monitor.Schedule(WatcherChangeTypes.Created, Path.Combine(Path.GetTempPath(), "new-folder"));
            Assert.IsTrue(asked.Wait(TimeSpan.FromSeconds(10)), "precondition: the monitor asked for a re-walk");
        }
        rig.Work.Run(0);

        Assert.AreEqual(2, rig.Work.Count, "the monitor's request was neither run nor turned into a rerun");
    }

    // The pending check and the Add used to run under two lock acquisitions. A queue landing between them won
    // the Add, and this request fell into the duplicate branch without being recorded. The gap is held open
    // through isRebuilding, the only callback that runs before the Add. It is re-entrant on this thread, which
    // reaches the same state as another thread queueing between the two locks.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void QueueDriveRebuild_AnotherQueueWinsBetweenCheckAndAdd_StillRerunsAfterIt(bool afterRemoval)
    {
        var drive = FreeDriveLetter();
        Rig? rig = null;
        var interleaved = false;
        using var created = rig = new Rig(isRebuilding: () =>
        {
            if (!interleaved)
            {
                interleaved = true;
                Assert.IsTrue(rig!.Maintenance.QueueDriveRebuild(drive, forceRebuild: false), "the competing queue");
            }
            return false;
        });

        if (afterRemoval)
            rig.Maintenance.QueueDriveRebuildAfterRemoval(drive);
        else
            rig.Maintenance.QueueDriveRebuild(drive); // the monitor's request
        Assert.AreEqual(1, rig.Work.Count, "precondition: the competing rebuild is the only one queued");
        rig.Work.Run(0);

        Assert.AreEqual(2, rig.Work.Count, "the request was neither run nor turned into a follow-up");
    }

    // A mounted drive, so a cancelled rebuild settles as "cached" rather than arming a re-insertion waiter that
    // would cover the request by other means. Every rebuild of it is cancelled before it runs.
    private static string MountedDrive()
    {
        var drive = VolumeHelper.DetectIndexableLocalDrives().FirstOrDefault(d => VolumeHelper.GetVolumeId(d) != null);
        if (drive == null)
            Assert.Inconclusive("No mounted drive with a volume id.");
        return drive;
    }

    // A full rebuild cancels the lifetime token, which cancels a per-drive rebuild that is still unwinding
    // (TryLoadDriveFromCache, say, takes no token). The full rebuild completes and then asks for this drive:
    // the initializer's flushed "ended initialization with no monitor" request, or a request from the new
    // monitor it started. The stale rebuild is still pending, so the request becomes its follow-up, and must
    // not be discarded with the cancelled walk: it waits as a retry.
    [TestMethod]
    public void QueueDriveRebuild_RaisedAfterAFullRebuildWhileTheCancelledRebuildUnwinds_IsNotDropped()
    {
        var drive = MountedDrive();
        var generation = new CancellationTokenSource();
        using var rig = new Rig(() => Selecting(drive), token: () => generation.Token);

        Assert.IsTrue(rig.Maintenance.QueueDriveRebuild(drive, forceRebuild: false)); // per-drive rebuild, generation 1
        generation.Cancel();                                                          // a full rebuild starts...
        generation = new CancellationTokenSource();                                   // ...and completes
        rig.Maintenance.QueueDriveRebuild(drive);                                     // its request for this drive
        rig.Work.Run(0);                                                              // the stale rebuild unwinds

        Assert.AreEqual(1, rig.Work.Count);
        Assert.IsTrue(IsWaiting(rig, drive), "a request raised during the cancelled rebuild was dropped");
        generation.Cancel(); // so the retry never reads the volume
    }

    // A Stop cancels the walk, not the losses reported to it: a request made before or after the Stop is a
    // monitor's loss (or a recording's), which only a walk after it recovers. It survives the Stop as a
    // waiting retry: the user asked for that walk to stop, so the next one does not start at once.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void QueueDriveRebuild_RaisedAroundAStop_IsKeptAsAWaitingRetry(bool beforeTheStop)
    {
        var drive = MountedDrive();
        using var rig = new Rig(() => Selecting(drive));

        Assert.IsTrue(rig.Maintenance.QueueDriveRebuild(drive, forceRebuild: false));
        if (beforeTheStop)
            rig.Maintenance.QueueDriveRebuild(drive);
        Assert.IsTrue(rig.Maintenance.CancelDriveRebuild(drive)); // Stop
        if (!beforeTheStop)
            rig.Maintenance.QueueDriveRebuild(drive);
        rig.Work.Run(0);

        Assert.AreEqual(1, rig.Work.Count, "the walk the user stopped started over at once");
        Assert.IsFalse(rig.Maintenance.HasPendingRebuilds);
        Assert.IsTrue(IsWaiting(rig, drive), "the request was dropped with the stopped walk");
    }

    // A per-drive rebuild hands its walk recording over inside RebuildDrive, while the drive is still pending.
    // A recording that lost events asks for one re-walk there, which used to be refused as a duplicate of the
    // very rebuild running it.
    [TestMethod]
    public void PerDriveHandOver_LostRecorder_ReWalkBecomesTheFollowUp()
    {
        using var rig = new Rig();
        var drive = FreeDriveLetter();
        using var build = new CancellationTokenSource();
        Assert.IsTrue(rig.Maintenance.QueueDriveRebuild(drive, forceRebuild: false));
        var recorder = new FolderWalkRecorder(drive, build.Token);
        recorder.MarkLost();
        FolderWalkRecorder.Keep(rig.Indexer, drive, recorder);

        FolderWalkRecorder.Replay(rig.Indexer, drive, build.Token, rig.Maintenance.QueueDriveRebuild); // EnsureMonitor
        rig.Work.Run(0);

        Assert.AreEqual(2, rig.Work.Count, "the hand-over's re-walk was refused as a duplicate of the rebuild running it");
    }

    // SettleFailedBuild raises the re-walk a live folder monitor left to the build from inside RebuildDrive, so
    // the drive is still pending then too. The drive has to be mounted for SettleFailedBuild to raise it, and a
    // mounted drive's rebuild cannot run here (it would read the volume), so that rebuild is stopped before it
    // runs: the follow-up it was asked for survives the Stop as a waiting retry.
    [TestMethod]
    public void SettleFailedBuild_DeferredReWalkRaisedInsideTheFailingRebuild_BecomesTheFollowUp()
    {
        var drive = MountedDrive();
        using var rig = new Rig(() => Selecting(drive));
        rig.Indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = drive, State = "ready", Enabled = true });
        using var build = new CancellationTokenSource();
        Assert.IsTrue(rig.Maintenance.QueueDriveRebuild(drive, forceRebuild: false));
        FolderWalkRecorder.Keep(rig.Indexer, drive, new FolderWalkRecorder(drive, build.Token));
        rig.Indexer.RequestFolderReindex(drive, _ => Assert.Fail("precondition: the request was left to the rebuild"));

        DriveArrivalRecovery.SettleFailedBuild(rig.Indexer, drive, wasCancelled: false, build.Token, rig.Lifetime.Token,
            null, rig.Maintenance.QueueDriveRebuild);
        Assert.IsTrue(rig.Maintenance.CancelDriveRebuild(drive));
        rig.Work.Run(0);

        Assert.AreEqual(1, rig.Work.Count);
        Assert.IsTrue(IsWaiting(rig, drive), "the deferred re-walk was raised into its own pending rebuild and refused");
    }

}
