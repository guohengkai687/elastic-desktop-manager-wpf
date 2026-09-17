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
        Loaded += (_, _) =>
        {
            TitleText.Text = Localization.L("nav.metrics");
        };
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (DataContext is MetricsViewModel vm)
            await vm.ReloadAsync();
    }
}
