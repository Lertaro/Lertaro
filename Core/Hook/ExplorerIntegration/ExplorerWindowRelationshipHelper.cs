namespace Lertaro.Core.Hook;

// Checks native window relationships for ExplorerWindowClassifier.
internal static class ExplorerWindowRelationshipHelper
{
    internal static bool IsDescendantOrOwned(IntPtr parent, IntPtr child)
    {
        if (parent == IntPtr.Zero || child == IntPtr.Zero) return false;
        if (parent == child) return true;

        var current = child;
        while (current != IntPtr.Zero)
        {
            if (current == parent) return true;
            var next = ExplorerNativeHooks.GetParent(current);
            if (next == IntPtr.Zero || next == current) break;
            current = next;
        }

        return ExplorerNativeHooks.GetAncestor(child, ExplorerNativeHooks.GA_ROOTOWNER) == parent;
    }
}
