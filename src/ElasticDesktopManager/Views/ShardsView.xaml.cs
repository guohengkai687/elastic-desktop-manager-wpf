using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;

namespace ElasticDesktopManager.Views;

public partial class ShardsView : UserControl
{
    private static readonly (int Index, string Key)[] HeaderMap =
    {
        (0, "shard.table.index"), (1, "shard.table.shard"), (2, "shard.table.prirep"), (3, "shard.table.state"),
        (4, "shard.table.docs"), (5, "shard.table.store"), (6, "shard.table.ip"), (7, "shard.table.node"),
    };

    public ShardsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            TitleText.Text = Localization.L("nav.shards");
            foreach (var (idx, key) in HeaderMap)
                if (idx < DataGrid.Columns.Count)
                    DataGrid.Columns[idx].Header = Localization.L(key);
        };
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.ShardsViewModel vm)
            _ = vm.ReloadAsync();
    }
}