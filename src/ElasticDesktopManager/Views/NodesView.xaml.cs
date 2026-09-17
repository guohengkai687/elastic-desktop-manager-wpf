using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class NodesView : UserControl
{
    /// <summary>
    /// 表头按下标对齐 XAML 的列顺序。XAML 里**不写**表头文案（只留列），
    /// 否则一处改语言、一处写死英文，迟早对不上；列数一致性由守卫规则核对。
    /// </summary>
    private static readonly (int Index, string Key)[] NodeHeaders =
    {
        (0, "node.table.name"), (1, "node.table.ip"), (2, "node.table.port"), (3, "node.table.version"),
        (4, "node.table.role"), (5, "node.table.master"), (6, "node.table.cpu"), (7, "node.table.heap"),
        (8, "node.table.ram"), (9, "node.table.disk"), (10, "node.table.load"), (11, "node.table.uptime"),
        (12, "node.table.jdk"),
    };

    public NodesView()
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
        (DataContext as PageViewModelBase)?.Relocalize();   // “节点统计：N” 是加载时拼好缓存的
    }

    private void Localize()
    {
        TitleText.Text = Localization.L("nav.nodes");
        RefreshButton.ToolTip = Localization.L("common.refresh");
        foreach (var (idx, key) in NodeHeaders)
            if (idx < NodeGrid.Columns.Count)
                NodeGrid.Columns[idx].Header = Localization.L(key);
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (DataContext is NodesViewModel vm)
            _ = vm.ReloadAsync();
    }
}
