using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Services;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class RestHistoryWindow : Window
{
    private readonly RestViewModel? _restVm;

    public RestHistoryWindow()
    {
        InitializeComponent();
        Owner = Ui.Main;
        _restVm = FindRestVm();
        Title = Localization.L("rest.history.title");
        DeleteButton.Content = Localization.L("common.delete");
        TipText.Text = Localization.L("rest.history.empty");
        EmptyHint.Text = Localization.L("rest.history.empty");

        Grid.Columns[0].Header = "Method";
        Grid.Columns[1].Header = Localization.L("rest.path");
        Grid.Columns[2].Header = Localization.L("rest.history.time");

        DataContext = new RestHistoryViewModel();
        ((RestHistoryViewModel)DataContext).Reload();
    }

    private static RestViewModel? FindRestVm()
    {
        return (Ui.Main?.DataContext as ViewModels.MainViewModel)?.GetRestViewModel();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        => DeleteButton.IsEnabled = Grid.SelectedItem is not null;

    private void OnRowDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is CommandHistoryItem item)
        {
            _restVm?.LoadFromHistory(item);
            Close();
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not CommandHistoryItem item) return;
        if (!Ui.Confirm(this, Localization.L("config.delete.prompt"))) return;
        App.HistoryService.Delete(item.Id);
        var vm = (RestHistoryViewModel)DataContext;
        vm.Reload();
        DeleteButton.IsEnabled = Grid.SelectedItem is not null;
    }
}