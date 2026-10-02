using System.Windows;
using System.Windows.Media.Animation;
using Lertaro.App.Views.Notifications;

namespace Lertaro.App.Services.Notifications;

/// <summary>
/// One shown notification: its window, and what is left of its time. Owned entirely by
/// <see cref="NotificationWindowManager"/>; the service reads it to run the countdown and never writes to it
/// except through that class.
/// </summary>
internal sealed class NotificationRunner(NotificationItem item, Window window)
{
    public NotificationItem Item { get; } = item;
    public Window Window { get; } = window;

    /// <summary>Milliseconds left of the notification's own display time. Hover and a locked session hold it;
    /// nothing else shortens or lengthens it.</summary>
    public double RemainingMs { get; set; } = item.DurationSeconds * 1000;

    /// <summary>Set when the window has never been on screen, and cleared by the first restack that places it.
    /// It is what turns that first placement into a drop into the slot rather than an arrival inside it.</summary>
    public bool Arriving { get; set; }

    /// <summary>The slide of the stack making room that is in charge of this card's Top, and where it is
    /// heading. A restack either leaves it running or takes it over, and this is how the card can tell which;
    /// the identity is there so a superseded animation recognises itself and keeps out of the way.</summary>
    public Slide? Moving { get; set; }

    internal readonly record struct Slide(DoubleAnimation Animation, double Target);

    /// <summary>A card the user dragged keeps the corner they left it in, until the screen under it changes.</summary>
    public bool IsPinnedByDrag => Window is NotificationCardWindow { IsUserMoved: true };

    public void ResetDrag()
    {
        if (Window is NotificationCardWindow card) card.ClearUserMove();
    }
}
