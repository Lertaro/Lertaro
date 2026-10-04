using System.Diagnostics;
using System.Windows;

namespace Lertaro.App.Tests.Views.Settings;

// Same class of bug the QuickPanel page test guards against: a StaticResource that does not exist, or
// a binding path that never resolves, compiles happily and fails silently at first render -- and this
// page's chapter switching runs entirely on RelativeSource bindings, so a typo there would leave every
// tab blank with nothing said out loud.
[TestClass]
[DoNotParallelize]
public sealed class SearchSyntaxSettingsPageTests
{
    [StaTestMethod]
    public void Page_BuildsWithEveryResourceItReferences()
    {
        var page = new Lertaro.App.Views.Settings.SearchSyntaxSettingsPage();

        Assert.IsNotNull(page.Content);
        Assert.AreEqual("Basic", page.SelectedChapter);
    }

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
            var page = new Lertaro.App.Views.Settings.SearchSyntaxSettingsPage();
            page.Measure(new Size(900, 700));
            page.Arrange(new Rect(0, 0, 900, 700));
            page.UpdateLayout();

            // Switching chapters is the page's only interaction; the triggers fire on this write.
            page.SelectedChapter = "Regex";
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
