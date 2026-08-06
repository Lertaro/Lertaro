using System.Collections.Specialized;
using System.Windows;
using Lertaro.App.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfListBox = System.Windows.Controls.ListBox;

namespace Lertaro.App.Views.LocalSend;

/// <summary>
/// UI helper methods for LocalSendSendWindow.xaml.cs.
/// ponytail: Split out purely to keep LocalSendSendWindow.xaml.cs under the repo's 300-line limit.
/// </summary>
public static class LocalSendSendWindowHelper
{
    public static void BindSelectAllButton(Window window)
    {
        if (window.FindName("BtnToggleSelectAll") is WpfButton button && window.FindName("LstDevices") is WpfListBox deviceList)
        {
            deviceList.Loaded += (_, _) => UpdateSelectAllButton(button, deviceList);
            ((INotifyCollectionChanged)deviceList.Items).CollectionChanged += (_, _) => UpdateSelectAllButton(button, deviceList);
        }
    }

    private static void UpdateSelectAllButton(WpfButton button, WpfListBox deviceList)
    {
        button.IsEnabled = deviceList.HasItems;
        if (!deviceList.HasItems)
        {
            deviceList.UnselectAll();
            button.Content = TranslationManager.Instance["Common_SelectAll"];
        }
    }
}
