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
        Loaded += (_, _) =>
        {
            TitleText.Text = Localization.L("nav.rest");
            BodyLabel.Text = Localization.L("rest.body");
            ResultLabel.Text = Localization.L("rest.result");
            NoDataHint.Text = Localization.L("rest.noData");
            ExamplesButton.Content = Localization.L("rest.examples");
            FormatButton.Content = Localization.L("rest.format");
            HistoryButton.Content = Localization.L("rest.history");
            ExecuteButton.Content = Localization.L("rest.execute");
        };
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