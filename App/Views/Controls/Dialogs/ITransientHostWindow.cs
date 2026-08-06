namespace Lertaro.App.Views.Controls.Dialogs;

/// <summary>
/// Marks a window that exists only to host something else for a moment, and so must never be picked as
/// a dialog's owner.
/// </summary>
/// <remarks>
/// A shell menu anchors itself on a 1x1 transparent window that lives exactly as long as the menu does.
/// Owning a dialog to that is owning it to something about to vanish, and WPF takes owned windows down
/// with their owner -- a prompt opened from a menu item would flash up and disappear again with the menu
/// that launched it, which is a bug this codebase has already met and worked around once, at a single
/// call site, by hiding the menu before opening the prompt (see QuickNavigationMenuContentExtensions).
///
/// Declared by the window rather than tested for by type in the resolver: the resolver has no business
/// knowing what a shell menu is, and the next short-lived host window should be able to say so itself
/// instead of waiting to be found out the same way.
/// </remarks>
internal interface ITransientHostWindow
{
}
