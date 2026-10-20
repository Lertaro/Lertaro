using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Lertaro.App.Services.Theme;
using Lertaro.App.Helpers.Visuals;
namespace Lertaro.App.Views.Controls.Dialogs;

public partial class CustomMessageBoxWindow : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public CustomMessageBoxWindow(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        InitializeComponent();
        // The window kind is fixed for the life of the handle, so it is declared opaque in XAML. Rounding
        // the real window is the whole of it: this dialog declares its own painted radius, and the nested
        // RoundedClip that keeps content inside it must keep agreeing with it.
        DwmWindowCorners.ApplyRound(this);

        SystemMenuBlocker.Attach(this);
        AltTabExcluder.Attach(this);
        ThemedWindowIconHelper.Apply(this);
        ThemedWindowIconHelper.Apply(TitleBarLogo);

        TxtTitle.Text = string.IsNullOrEmpty(caption) ? "Lertaro" : caption;
        TxtMessage.Text = messageBoxText;

        SetupIcon(icon);
        SetupButtons(button);
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            if (BtnOK.Visibility == Visibility.Visible &&
                BtnCancel.Visibility == Visibility.Collapsed &&
                BtnYes.Visibility == Visibility.Collapsed &&
                BtnNo.Visibility == Visibility.Collapsed)
            {
                Result = MessageBoxResult.OK;
            }
            Close();
        }
    }

    private void SetupButtons(MessageBoxButton button)
    {
        switch (button)
        {
            case MessageBoxButton.OK:
                BtnOK.Visibility = Visibility.Visible;
                BtnCancel.Visibility = Visibility.Collapsed;
                BtnYes.Visibility = Visibility.Collapsed;
                BtnNo.Visibility = Visibility.Collapsed;
                BtnOK.IsDefault = true;
                break;
            case MessageBoxButton.OKCancel:
                BtnOK.Visibility = Visibility.Visible;
                BtnCancel.Visibility = Visibility.Visible;
                BtnYes.Visibility = Visibility.Collapsed;
                BtnNo.Visibility = Visibility.Collapsed;
                BtnOK.IsDefault = true;
                BtnCancel.IsCancel = true;
                break;
            case MessageBoxButton.YesNo:
                BtnOK.Visibility = Visibility.Collapsed;
                BtnCancel.Visibility = Visibility.Collapsed;
                BtnYes.Visibility = Visibility.Visible;
                BtnNo.Visibility = Visibility.Visible;
                BtnYes.IsDefault = true;
                BtnNo.IsCancel = true;
                break;
            case MessageBoxButton.YesNoCancel:
                BtnOK.Visibility = Visibility.Collapsed;
                BtnCancel.Visibility = Visibility.Visible;
                BtnYes.Visibility = Visibility.Visible;
                BtnNo.Visibility = Visibility.Visible;
                BtnYes.IsDefault = true;
                BtnCancel.IsCancel = true;
                break;
        }
    }

    private void SetupIcon(MessageBoxImage icon)
    {
        switch (icon)
        {
            case MessageBoxImage.Error: // Hand, Stop
                TxtIcon.Text = "\uEA39";
                TxtIcon.SetResourceReference(TextBlock.ForegroundProperty, "ErrorBrush");
                TxtIcon.Visibility = Visibility.Visible;
                break;
            case MessageBoxImage.Warning: // Exclamation
                TxtIcon.Text = "\uE7BA";
                TxtIcon.SetResourceReference(TextBlock.ForegroundProperty, "WarningBrush");
                TxtIcon.Visibility = Visibility.Visible;
                break;
            case MessageBoxImage.Information: // Asterisk
                TxtIcon.Text = "\uE946";
                TxtIcon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBlue");
                TxtIcon.Visibility = Visibility.Visible;
                break;
            case MessageBoxImage.Question:
                TxtIcon.Text = "\uE9CE";
                TxtIcon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBlue");
                TxtIcon.Visibility = Visibility.Visible;
                break;
            default:
                TxtIcon.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); } catch { }
        }
    }

    private void BtnOK_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.OK;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Cancel;
        Close();
    }

    private void BtnYes_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Yes;
        Close();
    }

    private void BtnNo_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.No;
        Close();
    }

    public void SetCustomButtonTexts(string? okText, string? cancelText)
    {
        if (!string.IsNullOrEmpty(okText))
        {
            BtnOK.Content = okText;
            BtnOK.Width = double.NaN;
            BtnOK.Padding = new Thickness(14, 0, 14, 0);
        }
        if (!string.IsNullOrEmpty(cancelText))
        {
            BtnCancel.Content = cancelText;
            BtnCancel.Width = double.NaN;
            BtnCancel.Padding = new Thickness(14, 0, 14, 0);
        }
    }
}
