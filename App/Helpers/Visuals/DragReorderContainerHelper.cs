using System.Windows;
using System.Windows.Controls;

namespace Lertaro.App.Helpers.Visuals;

// Split out purely to keep DragReorder under the repository's per-file line limit. This helper has no
// state of its own and only resolves the realized item container for the one control passed to it.
internal static class DragReorderContainerHelper
{
    // Virtualizing panels realize only the containers a live mouse event can reach, so walking from
    // OriginalSource is both sufficient and avoids assuming a particular item template structure.
    public static FrameworkElement? Find(DependencyObject? source, ItemsControl control)
    {
        while (source != null && source != control)
        {
            if (source is FrameworkElement element && control.ItemContainerGenerator.IndexFromContainer(element) >= 0)
                return element;
            source = TreeWalk.Parent(source);
        }

        return null;
    }
}
