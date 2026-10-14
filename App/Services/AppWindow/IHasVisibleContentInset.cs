using System.Windows;

namespace Lertaro.App.Services.AppWindow;

/// <summary>
/// Implemented by windows that keep an invisible margin around their actual visible card. Lets code that
/// docks another window against this one (e.g. QuickLookManager) target the real visible edge instead of
/// the outer window's bounds.
/// </summary>
/// <remarks>
/// Every implementation returns zero now: the margin existed to hold a drop shadow inside a layered window,
/// and the windows are opaque with the card as the whole window rectangle (see WindowSurface). The interface
/// stays because the concept is what its callers depend on, and a window that ever reserves a margin again
/// needs somewhere to say so.
/// </remarks>
public interface IHasVisibleContentInset
{
    Thickness VisibleContentInset { get; }
}
