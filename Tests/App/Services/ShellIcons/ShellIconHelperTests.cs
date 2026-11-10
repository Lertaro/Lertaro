using System.Windows.Media.Imaging;
using System.Reflection;
using Lertaro.App.Services.ShellIcons;

namespace Lertaro.App.Tests.Services.ShellIcons;

[TestClass]
[DoNotParallelize]
public sealed class ShellIconHelperTests
{
    [TestInitialize]
    public void Setup() => ShellIconHelper.ClearCache();

    [TestCleanup]
    public void Cleanup() => ShellIconHelper.ClearCache();

    [StaTestMethod]
    public async Task GetIcon_RefreshKeepsPreviousImageUntilTheNewImageArrives()
    {
        var semaphore = (SemaphoreSlim)typeof(AppSearchResultDisplaySupport)
            .GetField("IconSemaphore", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var permits = 0;
        var refreshed = new TaskCompletionSource(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        var previous = new BitmapImage();
        previous.Freeze();
        var row = new AppSearchResult { FullPath = @"C:\missing-issue504\test.exe" };
        row.DisplayExtras.Icon = previous;
        row.DisplayExtras.IconNeedsRefresh = true;
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSearchResult.Icon)) refreshed.TrySetResult();
        };
        try
        {
            for (; permits < 4; permits++) Assert.IsTrue(await semaphore.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.AreSame(previous, row.Icon, "A cache miss must not replace the carried image with a placeholder.");
            Assert.IsTrue(row.ExistingExtras!.IconLoadingStarted);
            Assert.AreSame(previous, row.Icon, "Repeated binding reads must keep the image during loading.");
        }
        finally { if (permits > 0) semaphore.Release(permits); }

        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsNotNull(row.Icon);
        Assert.AreNotSame(previous, row.Icon, "The carried image must not freeze future icon changes.");
    }

    [StaTestMethod]
    public void GetIcon_CarriedImageIsReplacedByCurrentCachedImage()
    {
        var current = ShellIconHelper.GetIconFromCacheOnly(@"C:\folder\doc.txt", false, out var needsLoad);
        Assert.IsFalse(needsLoad);
        Assert.IsNotNull(current);
        var previous = new BitmapImage();
        var row = new AppSearchResult { FullPath = @"C:\folder\doc.txt" };
        row.DisplayExtras.Icon = previous;
        row.DisplayExtras.IconNeedsRefresh = true;

        Assert.AreSame(current, row.Icon);
        Assert.IsFalse(row.ExistingExtras!.IconNeedsRefresh);
        Assert.IsFalse(row.ExistingExtras.IconLoadingStarted);
        Assert.AreSame(current, row.Icon);
    }

    [StaTestMethod]
    public void GetIconFromCacheOnly_SpecialMarkers_ReturnsExpected()
    {
        var nullIcon = ShellIconHelper.GetIconFromCacheOnly("__NO_RESULTS__", false, out var needsLoadNoResults);
        Assert.IsNull(nullIcon);
        Assert.IsFalse(needsLoadNoResults);

        var showMoreIcon = ShellIconHelper.GetIconFromCacheOnly("__SHOW_MORE__", false, out var needsLoadShowMore);
        Assert.IsNotNull(showMoreIcon);
        Assert.IsFalse(needsLoadShowMore);
    }

    [StaTestMethod]
    public void GetIconFromCacheOnly_UniqueIconTypes_RequiresAsyncLoadAndReturnsPlaceholder()
    {
        // Directory requires background load and returns generic folder placeholder
        var dirIcon = ShellIconHelper.GetIconFromCacheOnly(@"C:\TestDir", true, out var needsLoadDir);
        Assert.IsTrue(needsLoadDir);
        Assert.IsNotNull(dirIcon);

        // Executable requires background load and returns placeholder
        var exeIcon = ShellIconHelper.GetIconFromCacheOnly(@"C:\Apps\test.exe", false, out var needsLoadExe);
        Assert.IsTrue(needsLoadExe);
        Assert.IsNotNull(exeIcon);

        // Shortcut requires background load and returns placeholder
        var lnkIcon = ShellIconHelper.GetIconFromCacheOnly(@"C:\Apps\test.lnk", false, out var needsLoadLnk);
        Assert.IsTrue(needsLoadLnk);
        Assert.IsNotNull(lnkIcon);
    }

    [StaTestMethod]
    public void GetIconFromCacheOnly_StandardDocumentExtension_LoadsSynchronouslyWithoutDiskProbe()
    {
        var icon1 = ShellIconHelper.GetIconFromCacheOnly(@"C:\folder\doc1.txt", false, out var needsLoad1);
        Assert.IsFalse(needsLoad1);
        Assert.IsNotNull(icon1);

        // Second file with the same extension shares the cached icon
        var icon2 = ShellIconHelper.GetIconFromCacheOnly(@"D:\other\doc2.txt", false, out var needsLoad2);
        Assert.IsFalse(needsLoad2);
        Assert.AreSame(icon1, icon2);
    }

    [StaTestMethod]
    public void GetIconFromCacheOnly_UncPaths_DoesNotBlockOrThrow()
    {
        // UNC document path returns standard extension icon without probing disk
        var uncDoc = ShellIconHelper.GetIconFromCacheOnly(@"\\remote-server\share\file.txt", false, out var needsLoadDoc);
        Assert.IsFalse(needsLoadDoc);
        Assert.IsNotNull(uncDoc);

        // UNC executable path marks needsLoad = true without probing disk on UI thread
        var uncExe = ShellIconHelper.GetIconFromCacheOnly(@"\\remote-server\share\tool.exe", false, out var needsLoadExe);
        Assert.IsTrue(needsLoadExe);
        Assert.IsNotNull(uncExe);
    }

    [StaTestMethod]
    public void Step_FailingExtraction_AnswersNullSoTheChainMovesOn() =>
        // The chain behind GetIconForPath relies on this: a leaf that throws must not become a null for the
        // row, which is what one try/catch around the whole chain used to do.
        Assert.IsNull(ShellIconHelper.Step(() => throw new InvalidOperationException("wedged leaf"), "embedded icon", @"C:\Apps\test.exe"));

    [StaTestMethod]
    public void Step_PassingExtraction_ReturnsItsIcon()
    {
        var image = new BitmapImage();

        Assert.AreSame(image, ShellIconHelper.Step(() => image, "embedded icon", @"C:\Apps\test.exe"));
    }
}
