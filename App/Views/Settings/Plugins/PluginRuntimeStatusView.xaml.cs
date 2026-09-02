namespace Lertaro.App.Views.Settings.Plugins;

public partial class PluginRuntimeStatusView : System.Windows.Controls.UserControl
{
    private static readonly IReadOnlyDictionary<string, string> HeaderTranslationKeys =
        new Dictionary<string, string>
        {
            ["Name"] = "Plugins_RuntimeStatusPlugin",
            ["InvocationCount"] = "Plugins_RuntimeStatusCalls",
            ["AverageElapsedMilliseconds"] = "Plugins_RuntimeStatusAverage",
            ["LastElapsedMilliseconds"] = "Plugins_RuntimeStatusLast",
            ["MaxElapsedMilliseconds"] = "Plugins_RuntimeStatusMax",
            ["AllocatedMegabytes"] = "Plugins_RuntimeStatusAllocated",
            ["ExceptionCount"] = "Plugins_RuntimeStatusExceptions"
        };

    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;

    public PluginRuntimeStatusView()
    {
        InitializeComponent();
        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += RefreshTimer_Tick;
        Loaded += (_, _) => _refreshTimer.Start();
        Unloaded += (_, _) => _refreshTimer.Stop();
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (DataContext is ViewModels.Settings.Plugins.PluginManagementViewModel viewModel)
            viewModel.RefreshRuntimeStatus();
    }

    private void RuntimeStatusHeader_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var header = FindHeader(e.OriginalSource as System.Windows.DependencyObject);
        if (header?.Tag is not string column
            || DataContext is not ViewModels.Settings.Plugins.PluginManagementViewModel viewModel)
            return;

        viewModel.SortRuntimeStatuses(column);
        UpdateSortIndicators(viewModel);
        e.Handled = true;
    }

    private void UpdateSortIndicators(ViewModels.Settings.Plugins.PluginManagementViewModel viewModel)
    {
        if (RuntimeStatusList.View is not System.Windows.Controls.GridView gridView)
            return;

        foreach (var column in gridView.Columns)
        {
            if (column.Header is not System.Windows.Controls.GridViewColumnHeader header
                || header.Tag is not string columnId
                || !HeaderTranslationKeys.TryGetValue(columnId, out var translationKey))
                continue;

            var indicator = viewModel.RuntimeStatusSortColumn == columnId
                ? viewModel.RuntimeStatusSortDescending ? " ▼" : " ▲"
                : string.Empty;
            header.Content = Services.TranslationManager.Instance[translationKey] + indicator;
        }
    }

    private static System.Windows.Controls.GridViewColumnHeader? FindHeader(System.Windows.DependencyObject? source)
    {
        while (source != null)
        {
            if (source is System.Windows.Controls.GridViewColumnHeader header)
                return header;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
