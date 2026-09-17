using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class RestView : UserControl
{
    public RestView()
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
        // REST 页的文案都是调用时才取词条的，这里不需要重算；
        // 仍然调一次是为了让“未连接”空态文案统一走同一条路（幂等、无副作用）。
        (DataContext as PageViewModelBase)?.Relocalize();
    }

    private void Localize()
    {
        TitleText.Text = Localization.L("nav.rest");
        BodyLabel.Text = Localization.L("rest.body");
        ResultLabel.Text = Localization.L("rest.result");
        NoDataHint.Text = Localization.L("rest.noData");
        ExamplesButton.Content = Localization.L("rest.examples");
        FormatButton.Content = Localization.L("rest.format");
        HistoryButton.Content = Localization.L("rest.history");
        ExecuteButton.Content = Localization.L("rest.execute");
    }

    private async void OnExecute(object sender, RoutedEventArgs e)
    {
        if (DataContext is RestViewModel vm)
            await vm.ExecuteCommand.ExecuteAsync(null);
    }

    private void OnFormat(object sender, RoutedEventArgs e)
    {
        if (DataContext is RestViewModel vm)
            vm.FormatCommand.Execute(null);
    }

    private void OnExamples(object sender, RoutedEventArgs e)
    {
        if (DataContext is RestViewModel vm)
            vm.OpenExamplesCommand.Execute(null);
    }

    private void OnHistory(object sender, RoutedEventArgs e)
    {
        if (DataContext is RestViewModel vm)
            vm.OpenHistoryCommand.Execute(null);
    }
}
