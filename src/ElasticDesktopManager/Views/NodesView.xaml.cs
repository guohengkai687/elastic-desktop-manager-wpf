using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;

namespace ElasticDesktopManager.Views;

public partial class NodesView : UserControl
{
    private static readonly (int Index, string Key)[] HeaderMap =
    {
        (0, "node.table.name"), (1, "node.table.ip"), (2, "node.table.port"), (3, "node.table.version"),
        (4, "node.table.role"), (5, "node.table.master"), (6, "node.table.cpu"), (7, "node.table.heap"),
        (8, "node.table.ram"), (9, "node.table.disk"), (10, "node.table.load"), (11, "node.table.uptime"),
        (12, "node.table.jdk"),
    };

    public NodesView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            TitleText.Text = Localization.L("nav.nodes");
            foreach (var (idx, key) in HeaderMap)
                if (idx < DataGrid.Columns.Count)
                    DataGrid.Columns[idx].Header = Localization.L(key);
        };
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.NodesViewModel vm)
            _ = vm.ReloadAsync();
    }
}