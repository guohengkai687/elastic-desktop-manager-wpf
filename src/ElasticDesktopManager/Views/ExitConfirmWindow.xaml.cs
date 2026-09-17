using System.Windows;
using ElasticDesktopManager.Core.I18n;

namespace ElasticDesktopManager.Views;

public partial class ExitConfirmWindow : Window
{
    public bool Minimize => MinimizeRadio.IsChecked == true;
    public bool Remember => RememberCheck.IsChecked == true;

    public ExitConfirmWindow()
    {
        InitializeComponent();
        TipTextBlock.Text = Localization.L("exit.tip.info");
        MinimizeRadio.Content = Localization.L("exit.tip.min");
        ExitRadio.Content = Localization.L("exit.tip.exit");
        RememberCheck.Content = Localization.L("exit.tip.remind");
        OkButton.Content = Localization.L("common.ok");
        CancelButton.Content = Localization.L("common.cancel");
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}