using Lertaro.App.Services.Favorites;
using Lertaro.Core;

namespace Lertaro.App.Tests.Services.Favorites;

// A fake window rather than a real one: the message-only HWND and RegisterHotKey are the untestable
// half of this feature, and the decisions worth pinning are which combinations get registered, under
// which ids, and what a fired id resolves to.
[TestClass]
[DoNotParallelize]
public sealed class FavoriteHotkeyServiceTests
{
    // The service keeps a process-wide Instance for the running app. Reset it before and after every
    // test so a leftover instance from another test can never change this one's outcome.
    [TestInitialize]
    public void ResetBefore() => FavoriteHotkeyService.Instance = null;

    [TestCleanup]
    public void ResetAfter() => FavoriteHotkeyService.Instance = null;

    private sealed class FakeHotkeyWindow : GlobalHotkeyWindow
    {
        public FakeHotkeyWindow(bool available = true) => IsAvailable = available;

        public override bool IsAvailable { get; }

        public Action<int>? Handler { get; private set; }
        public List<int> RegisterAttempts { get; } = new();
        public List<int> UnregisterCalls { get; } = new();

        /// <summary>Ids this fake refuses, standing in for a combination another application owns.</summary>
        public HashSet<int> RefuseIds { get; } = new();

        public override (bool Registered, int ErrorCode) Register(int id, uint modifiers, uint virtualKey)
        {
            // Mirrors the real window: with no receiver there is nothing to register against, so the
            // attempt never reaches the OS.
            if (!IsAvailable) return (false, 0);

            RegisterAttempts.Add(id);
            return RefuseIds.Contains(id) ? (false, 1409) : (true, 0);
        }

        public override void Unregister(int id) => UnregisterCalls.Add(id);

        public override void SetHandler(Action<int> onHotkeyId) => Handler = onHotkeyId;

        public void Fire(int hotkeyId) => Handler?.Invoke(hotkeyId);
    }

    private static FavoriteHotkeyRegistration Registered(int ownerIndex, string hotkey) =>
        new(ownerIndex, hotkey, 0x44, FavoriteHotkeyFormat.ToRegisterModifiers(System.Windows.Input.ModifierKeys.Control), FavoriteHotkeySkipReason.None);

    private static FavoriteHotkeyService CreateService(
        FakeHotkeyWindow window,
        List<string>? navigated = null,
        List<FavoriteItemSetting>? favorites = null)
    {
        var service = new FavoriteHotkeyService(
            windowFactory: () => window,
            register: window.Register,
            unregister: window.Unregister,
            navigate: path => navigated?.Add(path),
            favoriteAt: index => index >= 0 && index < (favorites?.Count ?? 0) ? favorites![index] : null);

        service.AttachHandler();
        return service;
    }

    [TestMethod]
    public void Refresh_RegistersEveryRequestInFavoriteOrder()
    {
        var window = new FakeHotkeyWindow();
        using var service = CreateService(window);

        service.Refresh([Registered(0, "Ctrl+1"), Registered(1, "Ctrl+2"), Registered(2, "Ctrl+3")]);

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, window.RegisterAttempts);
        Assert.AreEqual(0, service.FindOwnerIndex(1));
        Assert.AreEqual(1, service.FindOwnerIndex(2));
        Assert.AreEqual(2, service.FindOwnerIndex(3));
    }

    [TestMethod]
    public void Refresh_SkipsFavoritesWithNothingRegistrable()
    {
        var window = new FakeHotkeyWindow();
        using var service = CreateService(window);

        service.Refresh(
        [
            new FavoriteHotkeyRegistration(0, string.Empty, 0, 0, FavoriteHotkeySkipReason.Empty),
            Registered(1, "Ctrl+2"),
            new FavoriteHotkeyRegistration(2, "Ctrl", 0, 0, FavoriteHotkeySkipReason.Invalid),
            new FavoriteHotkeyRegistration(3, "Ctrl+2", 0x44, 2, FavoriteHotkeySkipReason.Duplicate)
        ]);

        // Only the one real request reached the OS, and it kept its own owner index.
        Assert.HasCount(1, window.RegisterAttempts);
        Assert.AreEqual(1, service.FindOwnerIndex(1));
        Assert.AreEqual(-1, service.FindOwnerIndex(2));
    }

    [TestMethod]
    public void Refresh_LeavesTheRestWorkingWhenOneCombinationIsRefused()
    {
        var window = new FakeHotkeyWindow();
        window.RefuseIds.Add(1);
        using var service = CreateService(window);

        var failures = new List<FavoriteHotkeyFailure>();
        service.Refresh([Registered(0, "Ctrl+1"), Registered(1, "Ctrl+2")], failures.AddRange);

        // A refused combination must not take the next id down with it: the second one still has to
        // register, under its own id, and still resolve to its own favorite.
        CollectionAssert.AreEqual(new[] { 1, 2 }, window.RegisterAttempts);
        Assert.HasCount(1, failures);
        Assert.AreEqual(0, failures[0].OwnerIndex);
        Assert.AreEqual("Ctrl+1", failures[0].Hotkey);
        Assert.AreEqual(1, service.FindOwnerIndex(2));
    }

    [TestMethod]
    public void Refresh_ReportsAnUnavailableCombinationOnlyWhileItIsUnavailable()
    {
        var window = new FakeHotkeyWindow();
        window.RefuseIds.Add(1);
        using var service = CreateService(window);

        service.Refresh([Registered(0, "Ctrl+1")]);
        Assert.IsTrue(service.IsNewlyUnavailable("Ctrl+1"));
        Assert.IsFalse(service.IsNewlyUnavailable("Ctrl+2"));
        Assert.IsFalse(service.IsNewlyUnavailable(null));

        // Rebuilt from scratch each time: a combination that registers now must stop reading as
        // unavailable, or the Settings page would keep showing an error for a working hotkey.
        window.RefuseIds.Clear();
        service.Refresh([Registered(0, "Ctrl+1")]);
        Assert.IsFalse(service.IsNewlyUnavailable("Ctrl+1"));
    }

    [TestMethod]
    public void Refresh_DropsThePreviousRegistrationsBeforeRegisteringTheNewList()
    {
        var window = new FakeHotkeyWindow();
        using var service = CreateService(window);

        service.Refresh([Registered(0, "Ctrl+1"), Registered(1, "Ctrl+2")]);
        service.Refresh([Registered(0, "Ctrl+3")]);

        // Everything the first refresh registered is dropped before the new list is registered, so no
        // combination is ever held twice.
        CollectionAssert.AreEqual(new[] { 1, 2 }, window.UnregisterCalls);
        Assert.AreEqual(1, window.RegisterAttempts[^1]);
        Assert.AreEqual(0, service.FindOwnerIndex(1));

        // The dropped combinations must no longer resolve: a stale id would navigate the wrong folder.
        Assert.AreEqual(-1, service.FindOwnerIndex(2));
    }

    [TestMethod]
    public void HandleHotkeyId_NavigatesTheFiredFavoritesOwnPath()
    {
        var window = new FakeHotkeyWindow();
        var navigated = new List<string>();
        var favorites = new List<FavoriteItemSetting>
        {
            new() { Path = @"C:\One" },
            new() { Path = @"C:\Two" }
        };
        using var service = CreateService(window, navigated, favorites);

        service.Refresh([Registered(0, "Ctrl+1"), Registered(1, "Ctrl+2")]);
        window.Fire(2);

        CollectionAssert.AreEqual(new[] { @"C:\Two" }, navigated);
    }

    [TestMethod]
    public void HandleHotkeyId_IgnoresAnIdThatBelongsToNoFavorite()
    {
        var window = new FakeHotkeyWindow();
        var navigated = new List<string>();
        using var service = CreateService(window, navigated, [new FavoriteItemSetting { Path = @"C:\One" }]);

        service.Refresh([Registered(0, "Ctrl+1")]);
        window.Fire(99);

        Assert.IsEmpty(navigated);
    }

    [TestMethod]
    public void HandleHotkeyId_IgnoresAnIndexWhoseFavoriteIsGone()
    {
        // The settings could have been edited after the registration was made; a vanished favorite must
        // not throw out of the window procedure.
        var window = new FakeHotkeyWindow();
        var navigated = new List<string>();
        using var service = CreateService(window, navigated, []);

        service.Refresh([Registered(0, "Ctrl+1")]);
        window.Fire(1);

        Assert.IsEmpty(navigated);
    }

    [TestMethod]
    public void Refresh_ReportsEveryFavoriteAsUnavailableWhenThereIsNoMessageWindow()
    {
        // The OS refusing the receiver window degrades the whole feature; nothing may throw, and the
        // user still gets told which rows did not take.
        var window = new FakeHotkeyWindow(available: false);
        using var service = CreateService(window);

        var failures = new List<FavoriteHotkeyFailure>();
        service.Refresh([Registered(0, "Ctrl+1")], failures.AddRange);

        Assert.HasCount(1, failures);
        Assert.IsEmpty(window.RegisterAttempts);
    }

    [TestMethod]
    public void Dispose_UnregistersEverythingItRegistered()
    {
        var window = new FakeHotkeyWindow();
        var service = CreateService(window);

        service.Refresh([Registered(0, "Ctrl+1"), Registered(1, "Ctrl+2")]);
        service.Dispose();

        CollectionAssert.AreEqual(new[] { 1, 2 }, window.UnregisterCalls);
        Assert.AreEqual(-1, service.FindOwnerIndex(1));
    }
}
