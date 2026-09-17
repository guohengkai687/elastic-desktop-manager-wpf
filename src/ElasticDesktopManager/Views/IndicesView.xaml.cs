using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class IndicesView : UserControl
{
    /// <summary>
    /// 表头按下标对齐 XAML 的列顺序。XAML 里**不写**表头文案（只留列），
    /// 否则一处改语言、一处写死英文，迟早对不上；列数一致性由守卫规则核对。
    /// </summary>
    private static readonly (int Index, string Key)[] IndexHeaders =
    {
        (0, "index.table.name"), (1, "index.table.health"), (2, "index.table.status"), (3, "index.table.shard"),
        (4, "index.table.docs"), (5, "index.table.store"), (6, "index.table.memory"), (7, "index.table.time"),
        (8, "index.table.action"),
    };

    public IndicesView()
    {
        InitializeComponent();
        Loaded += (_, _) => Localize();
        // 页面被 MainViewModel 缓存、切语言时不会重新 Loaded，必须显式订阅
        // （视图与应用同生命周期，无需解绑）。
        Localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        Localize();
        (DataContext as PageViewModelBase)?.Relocalize();   // “总数：N” / “第 N 页” 是 VM 拼的
    }

    private void Localize()
    {
        TitleText.Text = Localization.L("nav.indices");
        PrevButton.Content = "‹ " + Localization.L("sql.prevPage");
        NextButton.Content = Localization.L("sql.nextPage") + " ›";
        RefreshButton.ToolTip = Localization.L("common.refresh");
        foreach (var (idx, key) in IndexHeaders)
            if (idx < IndexGrid.Columns.Count)
                IndexGrid.Columns[idx].Header = Localization.L(key);
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
