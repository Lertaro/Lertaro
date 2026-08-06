using System.Diagnostics;
using System.Windows;

using Lertaro.App.ViewModels.Settings.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.Settings.QuickPanel;

// Constructing the page is the test. A StaticResource that does not exist compiles perfectly happily
// and only throws when the page is first built, which for a settings page nobody visits during a smoke
// run means shipping it -- this one shipped with a converter key that no dictionary in the app defines
// (BoolToVisibilityConverter, where the app's own key is BoolToVis) and the build said nothing.
// [DoNotParallelize] because the binding check below listens to PresentationTraceSources, which is
// process-wide: run alongside any other test that builds WPF elements and it collects that test's
// binding errors as if they were this page's. It passed alone and failed in a batch until this was
// added, which is exactly the shape the repo's shared-static-state rule warns about.
[TestClass]
[DoNotParallelize]
public sealed class QuickPanelSettingsPageTests
{
    [StaTestMethod]
    public void Page_BuildsWithEveryResourceItReferences()
    {
        var page = new Lertaro.App.Views.Settings.QuickPanel.QuickPanelSettingsPage
        {
            DataContext = new QuickPanelSettingsViewModel(new UserSettings())
        };

        Assert.IsNotNull(page.Content);
    }

    // The other half of the same class of bug, and the quieter half: a binding whose path does not
    // exist on the DataContext throws nothing and renders nothing -- a sub-tab that never switches, a
    // box that never fills. WPF only says so through its binding trace, so the test listens to it.
    [StaTestMethod]
    public void Page_LaysOutWithNoBrokenBindings()
    {
        var errors = new BindingErrorListener();
        var previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        try
        {
            var page = new Lertaro.App.Views.Settings.QuickPanel.QuickPanelSettingsPage
            {
                DataContext = new QuickPanelSettingsViewModel(new UserSettings())
            };
            page.Measure(new Size(900, 700));
            page.Arrange(new Rect(0, 0, 900, 700));
            page.UpdateLayout();
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(errors);
            PresentationTraceSources.DataBindingSource.Switch.Level = previousLevel;
        }

        Assert.IsEmpty(errors.Messages, string.Join(Environment.NewLine, errors.Messages));
    }

    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Messages { get; } = new();

        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message))
                Messages.Add(message);
        }
    }
}
