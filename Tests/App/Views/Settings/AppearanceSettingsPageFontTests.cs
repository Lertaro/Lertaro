using System.Windows;
using System.Windows.Controls;
using Lertaro.App.ViewModels.Settings.General;
using Lertaro.App.Views.Settings;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.Settings;

[TestClass]
public sealed class AppearanceSettingsPageFontTests
{
    // Loads the page itself rather than only the view model: the fallback rows live in a DataTemplate
    // whose static resources and ancestor bindings are resolved at runtime, so a markup mistake there
    // would compile and still leave the section empty.
    [StaTestMethod]
    public void PageRendersThreeFontFallbackSlotsInChainOrder()
    {
        var fonts = new UiFontSettingsViewModel(new UserSettings { UiFontFamilies = ["SimSun"] });
        var page = new AppearanceSettingsPage { DataContext = new SettingsStub(new AppearanceStub(fonts)) };
        // Offscreen and non-activating: bindings only attach once the tree has a PresentationSource,
        // but this assertion is about the generated rows, not about anything being visible.
        var host = new Window
        {
            Content = page,
            Width = 900,
            Height = 700,
            Left = -10000,
            Top = -10000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
        };
        try
        {
            host.Show();
            host.UpdateLayout();

            var list = Assert.IsInstanceOfType<ItemsControl>(page.FindName("FontSlotsList"));
            Assert.HasCount(3, list.Items);
            var slots = list.Items.Cast<UiFontSlot>().ToList();
            Assert.AreEqual("SimSun", slots[0].Family);
            Assert.IsNull(slots[1].Family);
            Assert.IsTrue(slots[0].CanChoose);
            Assert.IsTrue(slots[1].CanChoose, "the next empty slot must accept a font");
            Assert.IsFalse(slots[2].CanChoose, "a slot beyond the next empty one must stay disabled");
        }
        finally
        {
            host.Close();
        }
    }
}

public sealed class SettingsStub(AppearanceStub appearance)
{
    public AppearanceStub Appearance { get; } = appearance;
}

public sealed class AppearanceStub(UiFontSettingsViewModel fonts)
{
    public UiFontSettingsViewModel Fonts { get; } = fonts;
}
