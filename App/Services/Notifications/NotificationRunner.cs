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

    /// <summary>Milliseconds left of the notification's own display time. Hover and the session lock hold it;
    /// the fade-out is not part of it.</summary>
    public double RemainingMs { get; set; } = item.DurationSeconds * 1000;

    /// <summary>Set once the notification is on its way out, so nothing closes it twice or counts it down
    /// while it is already fading.</summary>
    public bool IsClosing { get; set; }

    public bool IsMoving { get; set; }

    /// <summary>Set once the fade-in has started, whether that came from the first rendered frame or from the
    /// watchdog armed alongside it.</summary>
    public bool FadeStarted { get; set; }

    /// <summary>Arrival order, which is what decides the vertical order of the stack.</summary>
    public int Sequence { get; } = Interlocked.Increment(ref _sequence);

    /// <summary>A card the user dragged keeps the corner they left it in, until the screen under it changes.</summary>
    public bool IsPinnedByDrag => Window is NotificationCardWindow { IsUserMoved: true };

    public void ResetDrag()
    {
        if (Window is NotificationCardWindow card) card.ClearUserMove();
    }
}
