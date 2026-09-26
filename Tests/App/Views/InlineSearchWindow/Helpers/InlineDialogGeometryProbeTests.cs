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

    private static InlineDialogGeometryProbe.Answer Measured() => new(Rect, Rect);

    private static InlineDialogGeometryProbe.Answer AnswerAt(int right) =>
        new(new ExplorerTracker.RECT { Left = 10, Top = 20, Right = right, Bottom = 44 },
            new ExplorerTracker.RECT { Left = 10, Top = 20, Right = right, Bottom = 44 });

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

        Assert.IsNull(probe.Request(Dialog, 100, 200, 400, 300).Anchor,
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

        Assert.IsNull(probe.Request(Dialog, 100, 200, 400, 300).Anchor);
        Assert.IsTrue(landed.Wait(Wait), "an answer that landed has to move the card again");

        var answer = probe.Request(Dialog, 100, 200, 400, 300);
        Assert.AreEqual(Rect.Right, answer.Anchor?.Right);
        Assert.AreEqual(Rect.Bottom, answer.FileList?.Bottom);

        for (var i = 0; i < 5; i++) probe.Request(Dialog, 100, 200, 400, 300);
        Assert.AreEqual(1, measures, "a placement pass must not re-ask what it already has");
    }

    [TestMethod]
    public void AMovedDialogIsMeasuredAgainWhileItsLastAnswerIsStillAllTheCardHas()
    {
        var measures = 0;
        var published = 0;
        using var secondEntered = new ManualResetEventSlim(false);
        using var secondMayReturn = new ManualResetEventSlim(false);
        var probe = new InlineDialogGeometryProbe(
            _ =>
            {
                // The second answer is held until the test lets it out, and each call returns its own
                // rectangle, so which of the two the card is placing itself against is observable.
                if (Interlocked.Increment(ref measures) == 1) return AnswerAt(210);
                secondEntered.Set();
                secondMayReturn.Wait(Wait);
                return AnswerAt(310);
            },
            () => Interlocked.Increment(ref published));

        Assert.IsNull(probe.Request(Dialog, 100, 200, 400, 300).Anchor);
        Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref published) == 1, Wait));
        Assert.AreEqual(210, probe.Request(Dialog, 100, 200, 400, 300).Anchor?.Right);

        // The dialog is dragged elsewhere. Its answer for the old place is what the card has, so it keeps
        // placing with that for the moment -- but it is not credited to the new rect, which is what the
        // second measurement proves, and it is exactly the credit that a cache keyed on the size alone
        // withheld: the card then anchors to where the dialog used to be, which looks like a card stuck
        // while the window moves.
        Assert.AreEqual(210, probe.Request(Dialog, 900, 560, 400, 300).Anchor?.Right);
        Assert.IsTrue(secondEntered.Wait(Wait), "the moved rect was never measured again");

        secondMayReturn.Set();
        Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref published) == 2, Wait));
        Assert.AreEqual(310, probe.Request(Dialog, 900, 560, 400, 300).Anchor?.Right, "the new rect's own answer wins");
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
            () => { probe.Request(Dialog, 100, 200, 400, 300); return Volatile.Read(ref measures) >= InlineDialogGeometryProbe.MaxAttemptsPerLayout; },
            Wait);
        Assert.IsTrue(gaveUp, "the dialog was never asked at all");

        for (var pass = 0; pass < 50; pass++) probe.Request(Dialog, 100, 200, 400, 300);
        Assert.AreEqual(InlineDialogGeometryProbe.MaxAttemptsPerLayout, measures, "the asking is bounded");
        Assert.AreEqual(0, published, "nothing landed, so the card should never be told to move");
    }

    [TestMethod]
    public void Invalidate_ReAsksALayoutTheProbeHadGivenUpOn()
    {
        // That budget gets spent on empty answers that were empty only because there was no adapter to
        // measure with at all. ExplorerTracker matches one on its own schedule, off the placing thread and
        // possibly seconds later, so a dialog that has not moved since must not stay centered under a layout
        // the probe already gave up on.
        var measures = 0;
        using var landed = new ManualResetEventSlim(false);
        var probe = new InlineDialogGeometryProbe(
            _ =>
            {
                // Nothing measurable while the budget lasts, exactly what a dialog with no adapter answers.
                if (Interlocked.Increment(ref measures) <= InlineDialogGeometryProbe.MaxAttemptsPerLayout)
                    return default;
                return Measured();
            },
            () => landed.Set());

        Assert.IsTrue(SpinWait.SpinUntil(
            () => { probe.Request(Dialog, 100, 200, 400, 300); return Volatile.Read(ref measures) >= InlineDialogGeometryProbe.MaxAttemptsPerLayout; },
            Wait));
        for (var pass = 0; pass < 5; pass++) probe.Request(Dialog, 100, 200, 400, 300);
        Assert.AreEqual(InlineDialogGeometryProbe.MaxAttemptsPerLayout, Volatile.Read(ref measures),
            "the layout was given up on, as the test above establishes it should be");

        probe.Invalidate();

        Assert.IsNull(probe.Request(Dialog, 100, 200, 400, 300).Anchor);
        Assert.IsTrue(landed.Wait(Wait), "the invalidated layout was never measured again");
        Assert.AreEqual(Rect.Right, probe.Request(Dialog, 100, 200, 400, 300).Anchor?.Right);
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
