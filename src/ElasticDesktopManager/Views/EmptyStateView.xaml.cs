using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;

namespace ElasticDesktopManager.Views;

public partial class EmptyStateView : UserControl
{
    public EmptyStateView()
    {
        InitializeComponent();
        Loaded += (_, _) => BtnConnect.Content = Localization.L("nav.connection");
    }

    private void OnClick(object sender, RoutedEventArgs e)
        => ElasticDesktopManager.Services.Ui.ShowConnections();
}