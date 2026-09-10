using System.Windows;

namespace Lertaro.App.Views.SearchWindow;

// Split out purely to keep SearchWindow under the repo's per-file line limit. Owns the full window's
// "hand the query back to the quick window, then close" move: it used to sit behind the back button
// only, but the global summon hotkey means the same thing when this window already holds foreground,
// and one implementation keeps the two routes from drifting apart.
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
