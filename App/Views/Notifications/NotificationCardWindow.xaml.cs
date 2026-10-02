using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Lertaro.App.Helpers.Visuals;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Views.Notifications;

/// <summary>
/// Position 1: one card in the bottom-right stack. The window is the card; the stack's layout is the
/// service's business, not this window's.
/// </summary>
public partial class NotificationCardWindow : Window
{
    // Long enough to be noticed as "the new one", short enough that a card still on screen for twenty seconds is
    // not wearing it. Not tuned against anything: it is a legibility choice, not a measurement.
    private const int ArrivalFlashMs = 750;

    /// <summary>Runs when the card should go away: its time ended, the user closed it, the user clicked
    /// its body, or the caller dismissed it through its handle.</summary>
    public event Action? DismissRequested;

    /// <summary>Runs when the user asks for the whole stack to be marked read.</summary>
    public event Action? ReadAllRequested;

    /// <summary>Set once the user drags the card: from then on the stack's re-layout leaves it alone,
    /// because taking back a position the user just chose would fight them for it.</summary>
    public bool IsUserMoved { get; private set; }

    public NotificationCardWindow()
    {
        InitializeComponent();
        // Chosen here because the theme decides the window kind and that choice is frozen with the handle.
        LayeredSurface.Apply(this, Surface);
    }

    public void SetContent(NotificationRequest request, string sourceName)
    {
        TxtTitle.Text = request.Title;
        TxtTitle.Visibility = string.IsNullOrWhiteSpace(request.Title) ? Visibility.Collapsed : Visibility.Visible;

        TxtBody.Text = request.Message;
        // A card with nothing but a title shows just the title line; the empty scroll area would otherwise
        // buy the card a strip of dead space in the stack.
        BodyScroll.Visibility = string.IsNullOrWhiteSpace(request.Message) ? Visibility.Collapsed : Visibility.Visible;

        TxtSource.Text = sourceName;
        TxtSource.Visibility = string.IsNullOrEmpty(sourceName) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(this, $"{sourceName}: {request.Title} {request.Message}".Trim());

        ApplyLevel(request.Level);
    }

    // SetResourceReference rather than an assignment, so a theme switch repaints what is already on screen
    // instead of leaving last theme's colour behind.
    private void ApplyLevel(NotificationLevel level)
    {
        var (glyph, barBrushKey) = level switch
        {
            NotificationLevel.Warn => ("\uE7BA", "WarningBrush"),
            NotificationLevel.Error => ("\uE783", "ErrorBrush"),
            // Info carries no edge bar at all: it is the level that needs nothing pointed at it.
            _ => ("\uE946", null),
        };
        LevelIcon.Text = glyph;
        LevelIcon.SetResourceReference(TextBlock.ForegroundProperty, level switch
        {
            NotificationLevel.Warn => "WarningBrush",
            NotificationLevel.Error => "ErrorBrush",
            // There is no Info semantic colour in this project's theme dictionaries, so the accent colour is
            // the one that already means "information" here.
            _ => "AccentBlue",
        });

        if (barBrushKey == null)
        {
            LevelBar.Visibility = Visibility.Collapsed;
            return;
        }

        LevelBar.SetResourceReference(Shape.FillProperty, barBrushKey);
        LevelBar.Visibility = Visibility.Visible;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || e.ClickCount != 1) return;
        try
        {
            DragMove();
            IsUserMoved = true;
        }
        catch (InvalidOperationException)
        {
            // DragMove only works from a mouse-down message; anything else that gets here is not a drag.
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => DismissRequested?.Invoke();

    private void BtnReadAll_Click(object sender, RoutedEventArgs e) => ReadAllRequested?.Invoke();

    /// <summary>Hands the card back to the stack's layout, for when the screen it was dragged onto is gone.</summary>
    public void ClearUserMove() => IsUserMoved = false;

    /// <summary>Marks the card as the one that has just come in: an accent rim is drawn around it and fades out
    /// over the next three quarters of a second.</summary>
    /// <remarks>
    /// The animation is on an element inside a window that is already opaque, which is the half of "notifications
    /// have no fade" this can afford. What was cut was animating the <see cref="Window"/>'s own opacity: that only
    /// works by turning the window into a layered one, which costs ClearType and a per-pixel composite on every
    /// frame, and flips state at both ends of the animation.
    /// </remarks>
    public void FlashArrival() => ArrivalRim.BeginAnimation(OpacityProperty,
        new DoubleAnimation(0, TimeSpan.FromMilliseconds(ArrivalFlashMs)) { From = 0.85 });

    // Clicking the body means "I have read it", which is the same outcome as the close button.
    private void Body_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => DismissRequested?.Invoke();
}
