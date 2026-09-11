using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Lertaro.App.Views.Settings;

// Prewarms the Settings window's not-yet-visited pages, one at a time, while the UI thread is idle.
//
// Split out purely to keep SettingsWindow under the repo's per-file line limit. It always operates on the
// window it is handed (reading its sidebar and calling its GetSectionPage), so the window keeps ownership of
// which page is which.
internal sealed class SettingsPagePrewarmer
{
    // By far the heaviest page: it reflects over every loaded plugin assembly. Built last so the cheap tabs
    // are warm first; if the user gets there first, the ordinary on-demand build runs instead (exactly what
    // happened before this existed), so making it the last thing built costs nothing.
    private const string HeaviestSection = "Plugins";

    private readonly SettingsWindow _window;

    internal SettingsPagePrewarmer(SettingsWindow window) => _window = window;

    /// <summary>The sidebars' section tags, in the order they should be prewarmed.</summary>
    /// <remarks>
    /// Taken from the sidebar entries themselves rather than a second list kept here, so a tab added to the
    /// window is covered without this needing to be updated. The heaviest section is moved to the end.
    /// </remarks>
    internal IEnumerable<string> SectionOrder()
    {
        var tags = new List<string>();
        Collect(_window.LstSections.Items);
        Collect(_window.LstSectionsBottom.Items);

        return tags.OrderBy(tag => string.Equals(tag, HeaviestSection, StringComparison.OrdinalIgnoreCase) ? 1 : 0);

        void Collect(ItemCollection items)
        {
            foreach (ListBoxItem item in items)
            {
                if (item.Tag is string tag && tag.Length > 0)
                    tags.Add(tag);
            }
        }
    }

    /// <summary>
    /// Builds each page in turn, one per idle slot, starting once the window has settled.
    /// </summary>
    /// <remarks>
    /// ApplicationIdle is what keeps this unobtrusive: the callback only runs when no input or render work is
    /// pending, so prewarming never competes with the user. One page per callback bounds the work in any
    /// single idle slot, and it is naturally idempotent -- the window's PageXxx properties memoize -- so a
    /// tab clicked before its turn simply takes the existing on-demand path instead.
    /// </remarks>
    internal void Begin()
    {
        var pending = SectionOrder().ToList();

        void PrewarmNext()
        {
            if (pending.Count == 0) return;

            var section = pending[0];
            pending.RemoveAt(0);

            BuildVisualTree(section);

            _window.Dispatcher.BeginInvoke(new Action(PrewarmNext), DispatcherPriority.ApplicationIdle);
        }

        // One hop of its own so the page the user is looking at actually paints before this starts.
        _window.Dispatcher.BeginInvoke(new Action(PrewarmNext), DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// Constructs the page AND runs a layout pass over it, so the tab is genuinely ready to show.
    /// </summary>
    /// <remarks>
    /// Constructing alone is not enough, which is the whole point of this method. Pages are parented
    /// Collapsed (see SettingsWindow.AddPage), and WPF skips measure/arrange entirely for a Collapsed
    /// element -- so the page's C# objects exist but its visual tree (the plugin list's item containers, the
    /// detail card, every form row) does not, and it all got built the first time the tab was actually
    /// shown. That was the delay being reported: the plugin page spent ~280ms realizing its tree on the
    /// click, despite having been "prewarmed".
    ///
    /// Briefly setting the page Hidden is what fixes it: Hidden still participates in layout (it is simply
    /// not painted or hit-testable), so an explicit UpdateLayout builds the tree right here, off the critical
    /// path. Measured on the plugin page: the first switch after prewarming dropped from ~211ms to ~2ms.
    /// A page that is already Visible is skipped -- flipping it would repaint what the user is looking at.
    /// </remarks>
    private void BuildVisualTree(string section)
    {
        var page = _window.GetSectionPage(section);
        if (page == null || page.Visibility == Visibility.Visible)
            return;

        page.Visibility = Visibility.Hidden;
        _window.UpdateLayout();
        page.Visibility = Visibility.Collapsed;
    }
}
