using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.LocalSend;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Lertaro.App.Views.LocalSend;

/// <summary>Lets the hosting send window select recipient devices and their optional PINs.</summary>
public partial class LocalSendDevicePanel : WpfUserControl
{
    public event EventHandler? CloseRequested;
    public event EventHandler<LocalSendDeviceSelectionEventArgs>? SendRequested;

    public LocalSendDevicePanel()
    {
        InitializeComponent();
        if (DeviceList.Items is INotifyCollectionChanged items)
            items.CollectionChanged += (_, _) => UpdateButtons();
        Loaded += (_, _) => UpdateButtons();
    }

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();
    private void Cancel_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void Send_Click(object sender, RoutedEventArgs e) => SendRequested?.Invoke(this, new LocalSendDeviceSelectionEventArgs(DeviceList.SelectedItems.OfType<LocalSendSendDeviceItem>().ToList()));

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItems.Count == DeviceList.Items.Count) DeviceList.UnselectAll(); else DeviceList.SelectAll();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        SendButton.IsEnabled = DeviceList.SelectedItems.Count > 0;
        SelectAllButton.IsEnabled = DeviceList.HasItems;
        SelectAllButton.Content = DeviceList.SelectedItems.Count == DeviceList.Items.Count && DeviceList.HasItems
            ? TranslationManager.Instance["Common_DeselectAll"] : TranslationManager.Instance["Common_SelectAll"];
    }
}

public sealed class LocalSendDeviceSelectionEventArgs(IReadOnlyList<LocalSendSendDeviceItem> devices) : EventArgs
{
    public IReadOnlyList<LocalSendSendDeviceItem> Devices { get; } = devices;
}
