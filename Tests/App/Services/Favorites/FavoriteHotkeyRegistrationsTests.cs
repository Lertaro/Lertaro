using Lertaro.App.Services.Favorites;
using Lertaro.Core;

namespace Lertaro.App.Tests.Services.Favorites;

[TestClass]
public sealed class FavoriteHotkeyRegistrationsTests
{
    [TestMethod]
    public void Build_KeepsOrderAndSkipsNothingWhenEveryFavoriteIsValid()
    {
        var favorites = new List<FavoriteItemSetting>
        {
            new() { Name = "One", Path = @"C:\One", Hotkey = "Ctrl+1" },
            new() { Name = "Two", Path = @"C:\Two", Hotkey = "Ctrl+2" }
        };

        var registrations = FavoriteHotkeyRegistrations.Build(favorites);

        Assert.HasCount(2, registrations);
        Assert.AreEqual(0, registrations[0].OwnerIndex);
        Assert.AreEqual("Ctrl+1", registrations[0].Hotkey);
        Assert.AreEqual(1, registrations[1].OwnerIndex);
        Assert.AreEqual(FavoriteHotkeySkipReason.None, registrations[0].SkipReason);
        Assert.AreEqual(FavoriteHotkeySkipReason.None, registrations[1].SkipReason);
    }

    [TestMethod]
    public void Build_UnsetHotkeyIsSkippedButStillReportedAsARow()
    {
        var registrations = FavoriteHotkeyRegistrations.Build([new FavoriteItemSetting { Path = @"C:\One", Hotkey = "" }]);

        Assert.HasCount(1, registrations);
        Assert.AreEqual(FavoriteHotkeySkipReason.Empty, registrations[0].SkipReason);
        Assert.AreEqual(string.Empty, registrations[0].Hotkey);
    }

    [TestMethod]
    public void Build_WhitespaceOnlyHotkeyCountsAsUnset()
    {
        var registrations = FavoriteHotkeyRegistrations.Build([new FavoriteItemSetting { Path = @"C:\One", Hotkey = "   " }]);

        Assert.AreEqual(FavoriteHotkeySkipReason.Empty, registrations[0].SkipReason);
    }

    [TestMethod]
    public void Build_InvalidFormatIsSkipped()
    {
        // "Ctrl" alone is the double-tap form the hook-owned hotkeys use -- there is no key for a
        // registration to fire on, so it must not become a registration request.
        var registrations = FavoriteHotkeyRegistrations.Build([new FavoriteItemSetting { Path = @"C:\One", Hotkey = "Ctrl" }]);

        Assert.AreEqual(FavoriteHotkeySkipReason.Invalid, registrations[0].SkipReason);
        Assert.AreEqual(0u, registrations[0].VirtualKey);
    }

    [TestMethod]
    public void Build_RepeatedComboIsKeptByTheFirstFavoriteOnly()
    {
        var favorites = new List<FavoriteItemSetting>
        {
            new() { Name = "First", Path = @"C:\First", Hotkey = "Ctrl+D" },
            new() { Name = "Second", Path = @"C:\Second", Hotkey = "Ctrl+D" }
        };

        var registrations = FavoriteHotkeyRegistrations.Build(favorites);

        Assert.AreEqual(FavoriteHotkeySkipReason.None, registrations[0].SkipReason);
        Assert.AreEqual(FavoriteHotkeySkipReason.Duplicate, registrations[1].SkipReason);

        // The duplicate keeps its parsed key so a caller can still show the row's combination, but the
        // skip reason is what stops it from being registered a second time.
        Assert.AreEqual(registrations[0].VirtualKey, registrations[1].VirtualKey);
    }

    [TestMethod]
    public void Build_RepeatedComboIsMatchedCaseInsensitively()
    {
        // Windows sees these as the same combination, so a case difference must not register twice.
        var favorites = new List<FavoriteItemSetting>
        {
            new() { Path = @"C:\First", Hotkey = "Ctrl+D" },
            new() { Path = @"C:\Second", Hotkey = "ctrl+d" }
        };

        var registrations = FavoriteHotkeyRegistrations.Build(favorites);

        Assert.AreEqual(FavoriteHotkeySkipReason.None, registrations[0].SkipReason);
        Assert.AreEqual(FavoriteHotkeySkipReason.Duplicate, registrations[1].SkipReason);
    }

    [TestMethod]
    public void Build_SkippedFavoritesDoNotClaimTheirCombination()
    {
        // The first row offers nothing registrable, so the second row's real combination must still be
        // accepted rather than being treated as a repeat of text nobody registered.
        var favorites = new List<FavoriteItemSetting>
        {
            new() { Path = @"C:\First", Hotkey = "Ctrl" },
            new() { Path = @"C:\Second", Hotkey = "Ctrl+E" }
        };

        var registrations = FavoriteHotkeyRegistrations.Build(favorites);

        Assert.AreEqual(FavoriteHotkeySkipReason.Invalid, registrations[0].SkipReason);
        Assert.AreEqual(FavoriteHotkeySkipReason.None, registrations[1].SkipReason);
    }

    [TestMethod]
    public void Build_DuplicateIsDroppedRegardlessOfHowManyFavoritesRepeatIt()
    {
        var favorites = new List<FavoriteItemSetting>
        {
            new() { Path = @"C:\First", Hotkey = "Alt+P" },
            new() { Path = @"C:\Second", Hotkey = "Alt+P" },
            new() { Path = @"C:\Third", Hotkey = "Alt+P" }
        };

        var registrations = FavoriteHotkeyRegistrations.Build(favorites);

        Assert.HasCount(3, registrations);
        Assert.AreEqual(FavoriteHotkeySkipReason.None, registrations[0].SkipReason);
        Assert.AreEqual(FavoriteHotkeySkipReason.Duplicate, registrations[1].SkipReason);
        Assert.AreEqual(FavoriteHotkeySkipReason.Duplicate, registrations[2].SkipReason);
    }

    [TestMethod]
    public void Build_NullListProducesNoRequests() =>
        Assert.IsEmpty(FavoriteHotkeyRegistrations.Build(null));

    [TestMethod]
    public void Build_TrimsTheStoredCombination()
    {
        var registrations = FavoriteHotkeyRegistrations.Build([new FavoriteItemSetting { Path = @"C:\One", Hotkey = "  Ctrl+G  " }]);

        Assert.AreEqual("Ctrl+G", registrations[0].Hotkey);
        Assert.AreEqual(FavoriteHotkeySkipReason.None, registrations[0].SkipReason);
    }
}
