using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class EmptyStateView : UserControl
{
    public EmptyStateView()
    {
        InitializeComponent();
        Loaded += (_, _) => Localize();
        // 本控件嵌在**缓存页面**里，切语言时不会重新 Loaded（MainViewModel.OnLanguageChanged
        // 只刷新导航标题与状态栏），必须显式订阅；视图与应用同生命周期，无需解绑。
        Localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        Localize();
        // 空态的两行文案（NotConnectedTitle/Hint）来自页面 VM 的属性，视图这边刷不到，
        // 让 VM 重发通知；页面宿主的语言处理器也会调一次，重复调用是幂等的。
        (DataContext as PageViewModelBase)?.Relocalize();
    }

    private void Localize() => BtnConnect.Content = Localization.L("nav.connection");

    private void OnClick(object sender, RoutedEventArgs e)
        => ElasticDesktopManager.Services.Ui.ShowConnections();
}
