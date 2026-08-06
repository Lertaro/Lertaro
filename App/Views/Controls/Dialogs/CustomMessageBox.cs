using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Lertaro.App.Views.Controls.Dialogs;

public static class CustomMessageBox
{
    public static MessageBoxResult Show(string messageBoxText) => Show(null, messageBoxText, "Lertaro", MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string messageBoxText, string caption) => Show(null, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button) => Show(null, messageBoxText, caption, button, MessageBoxImage.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) => Show(null, messageBoxText, caption, button, icon);

    public static MessageBoxResult Show(Window owner, string messageBoxText) => Show(owner, messageBoxText, "Lertaro", MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption) => Show(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button) => Show(owner, messageBoxText, caption, button, MessageBoxImage.None);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        if (Application.Current == null)
        {
            // Fallback to standard system MessageBox if WPF application is not active
            return MessageBox.Show(messageBoxText, caption, button, icon);
        }

        if (Application.Current.Dispatcher.CheckAccess())
        {
            return ShowInternal(owner, messageBoxText, caption, button, icon);
        }
        else
        {
            return Application.Current.Dispatcher.Invoke(() => ShowInternal(owner, messageBoxText, caption, button, icon));
        }
    }

    private static MessageBoxResult ShowInternal(Window? owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        var win = new CustomMessageBoxWindow(messageBoxText, caption, button, icon);

        // A caller-supplied owner is taken as given; otherwise the usual chain, declining to sit on
        // another message box the way it always has.
        win.Owner = owner ?? OwnedDialog.ResolveOwner(win, skip: w => w is CustomMessageBoxWindow);

        win.WindowStartupLocation = win.Owner != null
            ? WindowStartupLocation.CenterOwner
            : WindowStartupLocation.CenterScreen;

        // Not win.ShowDialog(): an owner closing while this is up would otherwise take the dialog's
        // window with it and leave the whole app frozen -- see OwnedDialog.ShowModal. Result stays
        // None in that case, which is what every caller already reads a dismissed box as.
        OwnedDialog.ShowModal(win);
        return win.Result;
    }

    public static MessageBoxResult ShowCustom(string messageBoxText, string caption, string okText, string cancelText, MessageBoxImage icon = MessageBoxImage.Information) =>
        ShowCustom(null, messageBoxText, caption, okText, cancelText, icon);

    public static MessageBoxResult ShowCustom(Window? owner, string messageBoxText, string caption, string okText, string cancelText, MessageBoxImage icon = MessageBoxImage.Information)
    {
        if (Application.Current == null)
        {
            return MessageBox.Show(messageBoxText, caption, MessageBoxButton.OKCancel, icon);
        }

        var action = new Func<MessageBoxResult>(() =>
        {
            var win = new CustomMessageBoxWindow(messageBoxText, caption, MessageBoxButton.OKCancel, icon);
            win.SetCustomButtonTexts(okText, cancelText);
            win.Owner = owner ?? OwnedDialog.ResolveOwner(win, skip: w => w is CustomMessageBoxWindow);
            win.WindowStartupLocation = win.Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            OwnedDialog.ShowModal(win);
            return win.Result;
        });

        return Application.Current.Dispatcher.CheckAccess() ? action() : Application.Current.Dispatcher.Invoke(action);
    }
}
