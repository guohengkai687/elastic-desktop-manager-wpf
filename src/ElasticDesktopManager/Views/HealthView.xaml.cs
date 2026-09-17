using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class HealthView : UserControl
{
    public HealthView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        // 页面被 MainViewModel 缓存、切语言时不会重新 Loaded，必须显式订阅
        // （视图与应用同生命周期，无需解绑）。
        Localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Localize();
        if (DataContext is HealthViewModel vm)
            vm.SetActive(true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // 注意：这里只关轮询，**不再**重复本地化 —— 文案统一走 Localize()
        if (DataContext is HealthViewModel vm)
            vm.SetActive(false);
    }

    private void OnLanguageChanged()
    {
        Localize();
        (DataContext as PageViewModelBase)?.Relocalize();   // 指标卡标签是 VM 拼好缓存的
    }

    private void Localize()
    {
        TitleText.Text = Localization.L("home.health.title");
        EsInfoTitle.Text = Localization.L("home.esInfo.title");
        AutoRefreshCheck.Content = Localization.L("home.autoRefresh");
        NodeLabel.Text = Localization.L("home.node");
        UuidLabel.Text = Localization.L("home.clusterUuid");
        VersionLabel.Text = Localization.L("home.version");
        RefreshButton.ToolTip = Localization.L("common.refresh");
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (DataContext is HealthViewModel vm)
            _ = vm.ReloadAsync();
    }
}
