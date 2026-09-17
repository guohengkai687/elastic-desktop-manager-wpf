using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class ShardsView : UserControl
{
    /// <summary>
    /// 表头按下标对齐 XAML 的列顺序。XAML 里**不写**表头文案（只留列），
    /// 否则一处改语言、一处写死英文，迟早对不上；列数一致性由守卫规则核对。
    /// </summary>
    private static readonly (int Index, string Key)[] ShardHeaders =
    {
        (0, "shard.table.index"), (1, "shard.table.shard"), (2, "shard.table.prirep"), (3, "shard.table.state"),
        (4, "shard.table.docs"), (5, "shard.table.store"), (6, "shard.table.ip"), (7, "shard.table.node"),
    };

    public ShardsView()
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
        (DataContext as PageViewModelBase)?.Relocalize();   // “分片统计：N” 是加载时拼好缓存的
    }

    private void Localize()
    {
        TitleText.Text = Localization.L("nav.shards");
        RefreshButton.ToolTip = Localization.L("common.refresh");
        foreach (var (idx, key) in ShardHeaders)
            if (idx < ShardGrid.Columns.Count)
                ShardGrid.Columns[idx].Header = Localization.L(key);
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShardsViewModel vm)
            _ = vm.ReloadAsync();
    }
}
