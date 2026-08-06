using System.Windows;

namespace Lertaro.App.Services.AppWindow;

/// <summary>
/// Implemented by windows that use AllowsTransparency with an invisible margin around their actual
/// visible card (room for a drop shadow). Lets code that docks another window against this one (e.g.
/// QuickLookManager) target the real visible edge instead of the outer window's transparent bounds.
/// </summary>
public interface IHasVisibleContentInset
{
    Thickness VisibleContentInset { get; }
}
