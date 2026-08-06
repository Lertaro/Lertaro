using System.Windows;
using System.Windows.Media;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>Walking up from whatever a mouse event actually started on.</summary>
/// <remarks>
/// <see cref="VisualTreeHelper.GetParent"/> throws on anything that is not a Visual, and a mouse event's
/// OriginalSource frequently is not one: text that has been highlighted -- a filter match, a search match
/// -- is a TextBlock split into Runs, and a Run is a ContentElement. So a click on the highlighted part
/// of a name took down the whole app, while the same click a millimetre to the side, on the unhighlighted
/// part of the same word, was fine.
///
/// One helper rather than a check bolted onto each walk: this has now been hit three times in three
/// places (the full window's input handler fixed its own copy long ago), and every one of them was a
/// hand-rolled loop over GetParent that simply had not met a Run yet.
/// </remarks>
internal static class TreeWalk
{
    /// <summary>One step up, crossing from the content tree back into the visual one where needed.</summary>
    public static DependencyObject? Parent(DependencyObject node)
        => node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node)
            : (node as FrameworkContentElement)?.Parent;

    /// <summary>The nearest ancestor of the given type, starting at the node itself.</summary>
    public static T? Ancestor<T>(DependencyObject? from) where T : DependencyObject
    {
        for (var node = from; node != null; node = Parent(node))
        {
            if (node is T match) return match;
        }
        return null;
    }
}
