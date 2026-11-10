namespace Lertaro.App.Views.Controls.Dialogs;

/// <summary>
/// Marks a window that exists only to host something else for a moment, and so must never be picked as
/// a dialog's owner.
/// </summary>
/// <remarks>
/// Shell and tray menus use tiny transparent windows. A reused anchor stays visible while parked
/// off screen; an owned dialog would be centred there. A disposable anchor can instead take its
/// owned windows down when it closes. Neither kind is a suitable dialog owner.
///
/// Declared by the window rather than tested for by type in the resolver: the resolver has no business
/// knowing what a shell menu is, and the next short-lived host window should be able to say so itself
/// instead of waiting to be found out the same way.
/// </remarks>
internal interface ITransientHostWindow
{
}
