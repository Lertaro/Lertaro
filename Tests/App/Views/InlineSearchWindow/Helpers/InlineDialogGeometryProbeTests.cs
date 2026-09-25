using System.IO;
using System.Threading;
using Lertaro.App.Views.InlineSearchWindow.Helpers;
using Lertaro.Core.Hook;

namespace Lertaro.App.Tests.Views.InlineSearchWindow.Helpers;

// The card is placed on the WPF thread, and asking a dialog's adapter from there is how a closing WPS dialog
// once froze the whole application: its answer comes through UI Automation, a synchronous call into a foreign
// process that stops answering when that process is busy or tearing the window down -- and waiting for the
// element in the first place is not what UI Automation's timeout bounds. So the probe must never measure on
// the thread that asks, must ask once per dialog layout instead of once per placement pass, and must not act
// on an answer that arrived for a layout the card has already left.
[TestClass]
public sealed class InlineDialogGeometryProbeTests
{
    private static readonly IntPtr Dialog = new(0x1234);
    private static readonly ExplorerTracker.RECT Rect = new() { Left = 10, Top = 20, Right = 210, Bottom = 44 };
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static InlineDialogGeometryProbe.Answer Measured() =>
        new(Rect, Rect);

    [TestMethod]
    public void TheAskingThreadIsNeverTheOneThatMeasures()
    {
        var askingThread = Environment.CurrentManagedThreadId;
        var measuredOn = 0;
        using var entered = new ManualResetEventSlim(false);
        using var mayReturn = new ManualResetEventSlim(false);
        var probe = new InlineDialogGeometryProbe(
            _ =>
            {
                Interlocked.Exchange(ref measuredOn, Environment.CurrentManagedThreadId);
                entered.Set();
                mayReturn.Wait(Wait);
                return Measured();
            },
            () => { });

        Assert.IsNull(probe.Request(Dialog, 400, 300).Anchor,
            "the answer cannot be known yet -- blocking here to wait for it is the freeze");

        Assert.IsTrue(entered.Wait(Wait), "nothing was ever measured, so the card would never get its anchor");
        Assert.AreNotEqual(askingThread, measuredOn);
        mayReturn.Set();
    }

    [TestMethod]
    public void OneLayoutIsMeasuredOnceAndTheAnswerSurvivesEveryLaterPass()
    {
        var measures = 0;
        using var landed = new ManualResetEventSlim(false);
        var probe = new InlineDialogGeometryProbe(
            _ => { Interlocked.Increment(ref measures); return Measured(); },
            () => landed.Set());

        Assert.IsNull(probe.Request(Dialog, 400, 300).Anchor);
        Assert.IsTrue(landed.Wait(Wait), "an answer that landed has to move the card again");

        var answer = probe.Request(Dialog, 400, 300);
        Assert.AreEqual(Rect.Right, answer.Anchor?.Right);
        Assert.AreEqual(Rect.Bottom, answer.FileList?.Bottom);

        for (var i = 0; i < 5; i++) probe.Request(Dialog, 400, 300);
        Assert.AreEqual(1, measures, "a placement pass must not re-ask what it already has");
    }

    [TestMethod]
    public void AnAnswerForTheSizeTheCardLeftIsNotApplied()
    {
        var measures = 0;
        var published = 0;
        using var firstEntered = new ManualResetEventSlim(false);
        using var firstMayReturn = new ManualResetEventSlim(false);
        using var secondEntered = new ManualResetEventSlim(false);
        using var secondMayReturn = new ManualResetEventSlim(false);
        var probe = new InlineDialogGeometryProbe(
            _ =>
            {
                // Both measurements are held until the test lets them answer, so the moment a stale one is
                // judged is observable rather than raced against the fresh one finishing.
                if (Interlocked.Increment(ref measures) == 1)
                {
                    firstEntered.Set();
                    firstMayReturn.Wait(Wait);
                }
                else
                {
                    secondEntered.Set();
                    secondMayReturn.Wait(Wait);
                }
                return Measured();
            },
            () => Interlocked.Increment(ref published));

        Assert.IsNull(probe.Request(Dialog, 400, 300).Anchor);
        Assert.IsTrue(firstEntered.Wait(Wait));

        // The dialog changed size while that measurement was still running.
        Assert.IsNull(probe.Request(Dialog, 800, 600).Anchor);
        Assert.AreEqual(1, measures, "one measurement at a time, however fast the dialog resizes");

        firstMayReturn.Set();
        Assert.IsTrue(SpinWait.SpinUntil(
            () => { probe.Request(Dialog, 800, 600); return Volatile.Read(ref measures) == 2; }, Wait),
            "the size the card is actually in has to be measured once the stale one is done");

        // A's answer is in hand now, and it is the wrong shape for the dialog the card is looking at: the
        // card keeps the anchorless placement rather than sliding under a rectangle from the old layout.
        Assert.IsNull(probe.Request(Dialog, 800, 600).Anchor, "an answer about a layout the card left is not applied");
        Assert.AreEqual(1, published, "a discarded answer still says the card should be placed again");

        secondMayReturn.Set();
        Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref published) == 2, Wait));
        Assert.AreEqual(Rect.Right, probe.Request(Dialog, 800, 600).Anchor?.Right);
        Assert.AreEqual(2, measures);
    }

    [TestMethod]
    public void ADialogWithNothingToAnswerIsAskedALimitedNumberOfTimes()
    {
        var measures = 0;
        var published = 0;
        var probe = new InlineDialogGeometryProbe(
            _ => { Interlocked.Increment(ref measures); return default; },
            () => Interlocked.Increment(ref published));

        // A dialog can answer for its window before it has laid out the widget inside it, so one empty answer
        // is not the last word -- but a dialog that genuinely has no anchor must not be asked on every
        // placement pass forever, so the asking stops after a bounded number of tries.
        var gaveUp = SpinWait.SpinUntil(
            () => { probe.Request(Dialog, 400, 300); return Volatile.Read(ref measures) >= InlineDialogGeometryProbe.MaxAttemptsPerLayout; },
            Wait);
        Assert.IsTrue(gaveUp, "the dialog was never asked at all");

        for (var pass = 0; pass < 50; pass++) probe.Request(Dialog, 400, 300);
        Assert.AreEqual(InlineDialogGeometryProbe.MaxAttemptsPerLayout, measures, "the asking is bounded");
        Assert.AreEqual(0, published, "nothing landed, so the card should never be told to move");
    }

    // The guard against the regression itself. The probe above cannot be reached from a test by the placement
    // path (it needs a live WPF window), and the freeze was caused by that path asking the dialog directly --
    // so what has to be pinned is that the placing thread asks the adapter nothing, and the only calls that
    // remain are the ones the probe makes from its own thread.
    [TestMethod]
    public void ThePlacingPathAsksTheDialogNothingDirectly()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "App/Views/InlineSearchWindow/Helpers/InlineSearchWindowPositioner.cs"));

        Assert.HasCount(1, LinesWith(source, "tracker.TryGetTargetFieldRect("),
            "the target field is read once, inside the probe's measure");
        Assert.HasCount(1, LinesWith(source, "tracker.TryGetFileListRect("),
            "and so is the file list");
        Assert.Contains("_geometry.Request(", source,
            "the placement itself has to go through the probe rather than the adapter");
    }

    private static List<string> LinesWith(string source, string needle) =>
        source.Split('\n').Where(line => line.Contains(needle, StringComparison.Ordinal)).Select(line => line.Trim()).ToList();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        return dir!.FullName;
    }
}
