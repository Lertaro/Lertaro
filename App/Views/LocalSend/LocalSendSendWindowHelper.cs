using System.Collections.Specialized;
using System.Windows;
using Lertaro.App.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfListBox = System.Windows.Controls.ListBox;

namespace Lertaro.App.Views.LocalSend;

/// <summary>
/// UI helpers for LocalSendSendWindow.
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
