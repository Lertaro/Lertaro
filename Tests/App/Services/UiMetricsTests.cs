using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

// UiMetrics holds process-wide mutable static state (Scale, PreviewWindowWidth, MainWindowWidth, ...),
// so every test here must run un-parallelized and reset that state afterward to avoid cross-test races
// (same pattern used for PluginSdk's static delegate seams elsewhere in this repo).
[TestClass]
[DoNotParallelize]
public sealed class UiMetricsTests
{
    [TestCleanup]
    public void Reset()
    {
        UiMetrics.Scale = 1.0;
        UiMetrics.UpdateScaleFromSearchBarHeight(UiMetrics.FlowRowReferenceSearchBarHeight);
        UiMetrics.PreviewWindowWidth = 400;
        UiMetrics.PreviewWindowHeight = 529;
        UiMetrics.MainWindowWidth = UiMetrics.DefaultMainWindowWidth;
        UiMetrics.MainWindowHeight = UiMetrics.DefaultMainWindowHeight;
    }

    [TestMethod]
    public void Scale_WithinRange_SetsExactValue()
    {
        UiMetrics.Scale = 1.2;

        Assert.AreEqual(1.2, UiMetrics.Scale);
    }

    [TestMethod]
    public void Scale_BelowMinimum_ClampsToMinimum()
    {
        UiMetrics.Scale = 0.1;

        Assert.AreEqual(0.6, UiMetrics.Scale);
    }

    [TestMethod]
    public void Scale_AboveMaximum_ClampsToMaximum()
    {
        UiMetrics.Scale = 5.0;

        Assert.AreEqual(1.8, UiMetrics.Scale);
    }

    [TestMethod]
    public void UpdateScaleFromSearchBarHeight_DefaultHeight_SetsScaleToOne()
    {
        UiMetrics.UpdateScaleFromSearchBarHeight(UiMetrics.DefaultSearchBarHeight);

        Assert.AreEqual(1.0, UiMetrics.Scale);
    }

    [TestMethod]
    public void UpdateScaleFromSearchBarHeight_LargerHeight_ScalesProportionally()
    {
        UiMetrics.UpdateScaleFromSearchBarHeight(105);

        Assert.AreEqual(1.5, UiMetrics.Scale);
    }

    [TestMethod]
    public void UpdateScaleFromSearchBarHeight_ZeroOrNegative_LeavesScaleUnchanged()
    {
        UiMetrics.Scale = 1.3;

        UiMetrics.UpdateScaleFromSearchBarHeight(0);
        Assert.AreEqual(1.3, UiMetrics.Scale);

        UiMetrics.UpdateScaleFromSearchBarHeight(-10);
        Assert.AreEqual(1.3, UiMetrics.Scale);
    }

    [TestMethod]
    public void UpdateScaleFromSearchBarHeight_AtFlowReferenceHeight_ScaledResultIconSizeMatchesFlowLiteral()
    {
        UiMetrics.UpdateScaleFromSearchBarHeight(UiMetrics.FlowRowReferenceSearchBarHeight);

        Assert.AreEqual(UiMetrics.FlowResultIconSize, UiMetrics.ScaledResultIconSize);
        Assert.AreEqual(UiMetrics.FlowResultItemHeight, UiMetrics.ScaledSearchResultItemHeight);
    }

    [TestMethod]
    public void PreviewWindowWidth_WithinRange_SetsExactValue()
    {
        UiMetrics.PreviewWindowWidth = 500;

        Assert.AreEqual(500, UiMetrics.PreviewWindowWidth);
    }

    [TestMethod]
    public void PreviewWindowWidth_OutOfRange_Clamps()
    {
        UiMetrics.PreviewWindowWidth = 10;
        Assert.AreEqual(UiMetrics.MinPreviewWindowWidth, UiMetrics.PreviewWindowWidth);

        UiMetrics.PreviewWindowWidth = 9999;
        Assert.AreEqual(UiMetrics.MaxPreviewWindowWidth, UiMetrics.PreviewWindowWidth);
    }

    [TestMethod]
    public void MainWindowHeight_OutOfRange_Clamps()
    {
        UiMetrics.MainWindowHeight = 10;
        Assert.AreEqual(UiMetrics.MinMainWindowHeight, UiMetrics.MainWindowHeight);

        UiMetrics.MainWindowHeight = 9999;
        Assert.AreEqual(UiMetrics.MaxMainWindowHeight, UiMetrics.MainWindowHeight);
    }

    [TestMethod]
    public void MenuItemHeight_IsEightyPercentOfListItemHeight() =>
        Assert.AreEqual(UiMetrics.BaseListItemHeight * 0.8, UiMetrics.MenuItemHeight);

    // InlineRowHeight/InlineIconSize are literal design constants (not derived from
    // BaseSearchResultItemHeight/a ratio) precisely so they CAN'T silently drift apart -- this pins the
    // one invariant that actually matters instead: the icon plus its margin must still fit inside the
    // row, or InlineSearchResult.xaml's icon would overflow InlineItemHeight's own row height.
    //
    // Written as a comparison assertion rather than IsTrue over a boolean: all three are const, so the
    // compiler folds the comparison and the analyzer rightly reports an IsTrue whose condition is always
    // true. It still guarded the invariant -- change a constant and the folded value becomes false --
    // but the dedicated assertion says the same thing without looking like a tautology, and reports the
    // two numbers when it fails instead of just "expected true".
    [TestMethod]
    public void InlineIconSize_FitsWithinInlineRowHeight() =>
        Assert.IsLessThanOrEqualTo(UiMetrics.InlineRowHeight, UiMetrics.InlineIconSize + UiMetrics.ResultRowVerticalMargin);

    [TestMethod]
    public void ScaledNormalRowHeight_AtFlowReferenceHeight_RowHeightWins()
    {
        // At/above the Flow reference height the row height (58 * scale) always outgrows the icon floor
        // (32 * scale + a small constant), since the row's own slope is larger.
        UiMetrics.UpdateScaleFromSearchBarHeight(UiMetrics.FlowRowReferenceSearchBarHeight);

        Assert.AreEqual(UiMetrics.ScaledSearchResultItemHeight, UiMetrics.ScaledNormalRowHeight);
    }

    [TestMethod]
    public void ScaledNormalRowHeight_AtVerySmallScale_IconFloorWins()
    {
        // At a small enough scale the icon floor's constant margin/breathing-room term dominates over
        // the linearly-shrinking row height, so ScaledNormalRowHeight must switch to the icon-floor branch.
        UiMetrics.UpdateScaleFromSearchBarHeight(15);

        var expectedFloor = UiMetrics.ScaledResultIconSize + UiMetrics.ResultRowVerticalMargin + UiMetrics.IconRowBreathingRoom;
        Assert.IsGreaterThan(UiMetrics.ScaledSearchResultItemHeight, expectedFloor);
        Assert.AreEqual(expectedFloor, UiMetrics.ScaledNormalRowHeight);
    }

    [TestMethod]
    public void ScaledQuickSearchMaxResultHeight_UsesVisibleRowBudget()
    {
        UiMetrics.UpdateScaleFromSearchBarHeight(UiMetrics.FlowRowReferenceSearchBarHeight);

        Assert.AreEqual(
            UiMetrics.QuickSearchMaxVisibleResultRows * UiMetrics.ScaledNormalRowHeight,
            UiMetrics.ScaledQuickSearchMaxResultHeight);
    }

    [TestMethod]
    [DataRow(300, 2)]
    [DataRow(400, 3)]
    [DataRow(570, 5)]
    [DataRow(800, 7)]
    public void GetLaunchPanelColumns_AdaptsToSearchBarWidth(double searchBarWidth, int expectedColumns) => Assert.AreEqual(expectedColumns, UiMetrics.GetLaunchPanelColumns(searchBarWidth));

    [TestMethod]
    public void Scale_SetToDifferentValue_RaisesScaleChanged()
    {
        UiMetrics.Scale = 1.0;
        var raised = false;
        void Handler() => raised = true;
        UiMetrics.ScaleChanged += Handler;
        try
        {
            UiMetrics.Scale = 1.4;
            Assert.IsTrue(raised);
        }
        finally
        {
            UiMetrics.ScaleChanged -= Handler;
        }
    }

    [TestMethod]
    public void Scale_SetToSameValue_DoesNotRaiseScaleChanged()
    {
        UiMetrics.Scale = 1.2;
        var raised = false;
        void Handler() => raised = true;
        UiMetrics.ScaleChanged += Handler;
        try
        {
            // Already exactly 1.2 -- an already-open window re-applying the same setting (e.g. Apply
            // clicked without changing anything) shouldn't trigger a needless refresh of every bound row.
            UiMetrics.Scale = 1.2;
            Assert.IsFalse(raised);
        }
        finally
        {
            UiMetrics.ScaleChanged -= Handler;
        }
    }

    [TestMethod]
    public void Scale_SetToOutOfRangeValueClampingToSameCurrentValue_DoesNotRaiseScaleChanged()
    {
        UiMetrics.Scale = 0.6;
        var raised = false;
        void Handler() => raised = true;
        UiMetrics.ScaleChanged += Handler;
        try
        {
            // Clamps down to the same 0.6 the value is already at -- the comparison must happen AFTER
            // clamping, not on the raw input, or this would incorrectly fire for a no-op change.
            UiMetrics.Scale = 0.1;
            Assert.IsFalse(raised);
        }
        finally
        {
            UiMetrics.ScaleChanged -= Handler;
        }
    }
}
