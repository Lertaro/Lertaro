using System.Windows;
using Lertaro.App.Views.Notifications;

namespace Lertaro.App.Services.Notifications;

/// <summary>
/// One shown notification: its window, and what is left of its time. Owned entirely by
/// <see cref="NotificationWindowManager"/>; the service reads it to run the countdown and never writes to it
/// except through that class.
/// </summary>
internal sealed class NotificationRunner(NotificationItem item, Window window)
{
    private static int _sequence;

    public NotificationItem Item { get; } = item;
    public Window Window { get; } = window;

    /// <summary>Milliseconds left of the notification's own display time. Hover and a locked session hold it;
    /// nothing else shortens or lengthens it.</summary>
    public double RemainingMs { get; set; } = item.DurationSeconds * 1000;

    public bool IsMoving { get; set; }

    /// <summary>Arrival order, which is what decides the vertical order of the stack.</summary>
    public int Sequence { get; } = Interlocked.Increment(ref _sequence);

    /// <summary>A card the user dragged keeps the corner they left it in, until the screen under it changes.</summary>
    public bool IsPinnedByDrag => Window is NotificationCardWindow { IsUserMoved: true };

    public void ResetDrag()
    {
        if (Window is NotificationCardWindow card) card.ClearUserMove();
    }
}
