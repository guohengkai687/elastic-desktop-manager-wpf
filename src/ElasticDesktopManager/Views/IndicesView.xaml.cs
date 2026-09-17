using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class IndicesView : UserControl
{
    private static readonly (int Index, string Key)[] HeaderMap =
    {
        (0, "index.table.name"), (1, "index.table.health"), (2, "index.table.status"), (3, "index.table.shard"),
        (4, "index.table.docs"), (5, "index.table.store"), (6, "index.table.memory"), (7, "index.table.time"),
        (8, "index.table.action"),
    };

    public IndicesView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            TitleText.Text = Localization.L("nav.indices");
            PrevButton.Content = "‹ " + Localization.L("sql.prevPage");
            NextButton.Content = Localization.L("sql.nextPage") + " ›";
            foreach (var (idx, key) in HeaderMap)
                if (idx < DataGrid.Columns.Count)
                    DataGrid.Columns[idx].Header = Localization.L(key);
        };
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (DataContext is IndicesViewModel vm)
            _ = vm.ReloadAsync();
    }

    /// <summary>动态构建本地化的“更多操作”菜单。</summary>
    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not IndicesViewModel vm || sender is not FrameworkElement btn) return;
        var row = btn.DataContext;

        var menu = new ContextMenu();
        AddItem(menu, vm.DetailsCommand, row, Localization.L("index.detail"));
        AddItem(menu, vm.StatsCommand, row, Localization.L("index.stats"));
        menu.Items.Add(new Separator());
        // 索引工具：Mapping/Settings/别名/字段Top值/维护/迁移（对应 ES-King 的索引管理能力）
        var tools = new MenuItem
        {
            Header = Localization.L("indextools.title"),
            Command = new RelayCommand(_ =>
            {
                if (row is ElasticDesktopManager.Core.Models.EsIndex idx && !string.IsNullOrEmpty(idx.Index))
                    Services.Ui.ShowIndexTools(idx.Index);
            }),
        };
        menu.Items.Add(tools);
        menu.Items.Add(new Separator());
        AddItem(menu, vm.RefreshIndexCommand, row, Localization.L("index.refreshIdx"));
        AddItem(menu, vm.FlushCommand, row, Localization.L("index.flush"));
        AddItem(menu, vm.CleanCacheCommand, row, Localization.L("index.cleanCache"));
        menu.Items.Add(new Separator());
        AddItem(menu, vm.OpenCommand, row, Localization.L("index.open"));
        AddItem(menu, vm.CloseCommand, row, Localization.L("index.close"));

        menu.PlacementTarget = btn;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static void AddItem(ContextMenu menu, ICommand command, object? parameter, string header)
    {
        var item = new MenuItem { Header = header, Command = command, CommandParameter = parameter };
        menu.Items.Add(item);
    }
}