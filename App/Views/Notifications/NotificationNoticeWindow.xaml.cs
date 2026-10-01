using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Helpers.Visuals;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Views.Notifications;

/// <summary>
/// Position 2: the single line at the bottom centre of the screen. No title, no source, no controls.
/// </summary>
public partial class NotificationNoticeWindow : Window
{
    public NotificationNoticeWindow()
    {
        InitializeComponent();
        LayeredSurface.Apply(this, Surface);
    }

    /// <summary>The widest the line may get, in DIP: a third of the work area, past which the text ellipsizes.</summary>
    public void ConsumeWidth(double maxTextWidthDip) => Line.MaxWidth = Math.Max(40, maxTextWidthDip);

    public void SetContent(string message, NotificationLevel level)
    {
        Line.Text = message;
        // The text colour is the only thing that carries the level here: there is no room for an icon in a
        // 36 DIP pill, and leaving Info on the plain reading colour keeps a run of notices from turning
        // entirely blue. SetResourceReference rather than an assignment so a theme switch mid-display
        // repaints this line along with everything else.
        Line.SetResourceReference(TextBlock.ForegroundProperty, level switch
        {
            NotificationLevel.Warn => "WarningBrush",
            NotificationLevel.Error => "ErrorBrush",
            _ => "TextPrimary",
        });
    }
}
