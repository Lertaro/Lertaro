namespace Lertaro.App.Tests.Views.InlineSearchWindow.Helpers;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Threading;
using Lertaro.App.Converters;
using Lertaro.App.Views.InlineSearchWindow.Helpers;

[TestClass]
public sealed class InlinePathDisplayBehaviorTests
{
    // The card's name cell takes its text from a Style-setter binding, and this behavior writes the same
    // attached property from code while an opened-folder row is displayed. IsEnabled is what the row
    // template's DataTrigger flips on ResultKind; the list virtualizes with Recycling, so the container
    // that painted an opened folder's path is the one that shows the next query's match -- and the card
    // is one window for the whole session, so whatever that cell keeps saying is what the user reads for
    // every later query and every later host.
    [STATestMethod]
    public void TheNameCellGoesBackToItsBindingWhenTheRowStopsBeingAnOpenedFolder()
    {
        var host = new Grid { Width = 80, Height = 100 };
        var nameCell = new TextBlock { FontSize = 14 };
        host.Children.Add(nameCell);
        host.Measure(new Size(80, 100));
        host.Arrange(new Rect(0, 0, 80, 100));

        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextHighlighter.TextProperty, new Binding(nameof(AppSearchResult.Name))));
        nameCell.Style = style;

        nameCell.DataContext = new AppSearchResult
        {
            Name = @"C:\123pan\a\b\c",
            FullPath = @"C:\123pan\a\b\c",
            ParentDir = string.Empty,
            IsDir = true,
            ResultKind = "OpenedFolder"
        };
        InlinePathDisplayBehavior.SetIsEnabled(nameCell, true);
        nameCell.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Pump();

        // Compacted, not the full path: proof that the behavior (not just the binding) wrote this cell.
        Assert.AreNotEqual(@"C:\123pan\a\b\c", TextOf(nameCell),
            "the opened-folder row shows the compacted path in the name cell");

        // What recycling does: the container is handed the next query's row, and the template's trigger
        // takes IsEnabled back off.
        nameCell.DataContext = new AppSearchResult
        {
            Name = "图形图像",
            FullPath = @"C:\Tools Backup\图形图像",
            ParentDir = @"C:\Tools Backup",
            IsDir = true,
            ResultKind = "File"
        };
        InlinePathDisplayBehavior.SetIsEnabled(nameCell, false);
        Pump();

        Assert.AreEqual("图形图像", TextOf(nameCell));
    }

    private static string TextOf(TextBlock textBlock) =>
        string.Concat(textBlock.Inlines.OfType<Run>().Select(run => run.Text));

    // The behavior queues its refresh at Loaded priority, so the dispatcher has to run past that.
    private static void Pump() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
