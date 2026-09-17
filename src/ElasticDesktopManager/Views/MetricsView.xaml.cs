using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class MetricsView : UserControl
{
    public MetricsView()
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
        (DataContext as PageViewModelBase)?.Relocalize();   // 分组标题 + “指标总数” 摘要
    }

    private void Localize()
    {
        TitleText.Text = Localization.L("nav.metrics");
        RefreshButton.ToolTip = Localization.L("common.refresh");
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (DataContext is MetricsViewModel vm)
            await vm.ReloadAsync();
    }
}
