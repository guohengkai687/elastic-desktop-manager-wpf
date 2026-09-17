using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;

namespace ElasticDesktopManager.Views;

public partial class HealthView : UserControl
{
    public HealthView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TitleText.Text = Localization.L("home.health.title");
        EsInfoTitle.Text = Localization.L("home.esInfo.title");
        AutoRefreshCheck.Content = Localization.L("home.autoRefresh");
        NodeLabel.Text = Localization.L("home.node");
        UuidLabel.Text = Localization.L("home.clusterUuid");
        VersionLabel.Text = Localization.L("home.version");
        if (DataContext is ViewModels.HealthViewModel vm)
            vm.SetActive(true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.HealthViewModel vm)
            vm.SetActive(false);
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.HealthViewModel vm)
            _ = vm.ReloadAsync();
    }
}