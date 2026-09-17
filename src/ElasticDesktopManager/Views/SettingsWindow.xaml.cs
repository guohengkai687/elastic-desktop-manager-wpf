using System.Windows;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Services;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow()
    {
        InitializeComponent();
        Owner = Ui.Main;
        _vm = new SettingsViewModel();
        DataContext = _vm;

        Title = Localization.L("setting.title");
        Localize();
        Localization.LanguageChanged += Localize;
    }

    private void Localize()
    {
        LanguageLabel.Text = Localization.L("setting.language");
        ThemeLabel.Text = Localization.L("setting.theme");
        AutoThemeCheck.Content = Localization.L("setting.autoTheme");
        OpenDialogCheck.Content = Localization.L("setting.openDialog");
        TimeoutLabel.Text = Localization.L("setting.timeout");
        CloseLabel.Text = Localization.L("setting.closeBehavior");
        CloseRememberCheck.Content = Localization.L("exit.tip.remind");
        CancelButton.Content = Localization.L("common.cancel");
        SaveButton.Content = Localization.L("setting.save");

        _vm.CloseOptions[0] = new("ask", Localization.L("setting.close.ask"));
        _vm.CloseOptions[1] = new("minimize", Localization.L("setting.close.minimize"));
        _vm.CloseOptions[2] = new("exit", Localization.L("setting.close.exit"));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _vm.SaveCommand.Execute(null);
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        Localization.LanguageChanged -= Localize;
        base.OnClosed(e);
    }
}