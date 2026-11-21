using System.Windows;
using System.Windows.Controls;

namespace Lertaro.App.Helpers.Visuals;

// Resolves the realized item container for a control.
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
