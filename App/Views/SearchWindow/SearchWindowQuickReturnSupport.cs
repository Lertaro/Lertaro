using System.Windows;

namespace Lertaro.App.Views.SearchWindow;

// Returns the full window's query to Quick Search and closes the full window. Shared by the back button
// and the repeated summon hotkey.
internal static class SearchWindowQuickReturnSupport
{
    internal static void ReturnToQuickSearch(Lertaro.App.SearchWindow window)
    {
        var quickSearchWindow = System.Windows.Application.Current.MainWindow as Lertaro.App.QuickSearchWindow;
        if (quickSearchWindow == null)
        {
            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is Lertaro.App.QuickSearchWindow qsw)
                {
                    quickSearchWindow = qsw;
                    break;
                }
            }
        }

        if (quickSearchWindow != null)
        {
            // This window's own query wins; the quick window's last one is the fallback for when the
            // full window was opened without any text of its own.
            var query = !string.IsNullOrWhiteSpace(window.SearchText)
                ? window.SearchText
                : quickSearchWindow.ViewModel.SearchQuery;

            // caretAtEnd: the query is carried over rather than retyped, so the next keystroke should
            // append to it. Leaving the caret at position 0 put it in front of the text the user already
            // had and made the first key they pressed after coming back land in the wrong place.
            quickSearchWindow.ShowWindow(query, caretAtEnd: true);
        }

        window.Close();
    }
}
