using System.Windows;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Lertaro.App.Views.LocalSend;

/// <summary>Shows sender-side per-file progress and waits for recipient confirmations.</summary>
public partial class LocalSendSendProgressPanel : WpfUserControl
{
    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(LocalSendSendProgressPanel));
    public event EventHandler? ActionRequested;

    public LocalSendSendProgressPanel() => InitializeComponent();
    public string ActionText { get => (string)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    private void Action_Click(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(this, EventArgs.Empty);
}
