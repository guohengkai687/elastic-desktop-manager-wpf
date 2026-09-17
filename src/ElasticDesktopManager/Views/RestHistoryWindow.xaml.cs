using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Services;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class RestHistoryWindow : Window
{
    /// <summary>表头按下标对齐 XAML 的列顺序（列数一致性由守卫规则核对）。</summary>
    private static readonly (int Index, string Key)[] HistoryHeaders =
    {
        (0, "rest.method"), (1, "rest.path"), (2, "rest.history.time"),
    };

    private readonly RestViewModel? _restVm;

    public RestHistoryWindow()
    {
        InitializeComponent();
        Owner = Ui.Main;
        _restVm = FindRestVm();
        // 窗口每次都是新建的，构造时取到的就是当前语言，因此不需要订阅 LanguageChanged
        // （订阅反而会让静态事件永久持有已关闭的窗口）。
        Title = Localization.L("rest.history.title");
        DeleteButton.Content = Localization.L("common.delete");
        TipText.Text = Localization.L("rest.history.empty");
        EmptyHint.Text = Localization.L("rest.history.empty");

        foreach (var (idx, key) in HistoryHeaders)
            if (idx < HistoryGrid.Columns.Count)
                HistoryGrid.Columns[idx].Header = Localization.L(key);

        DataContext = new RestHistoryViewModel();
        ((RestHistoryViewModel)DataContext).Reload();
    }

    private static RestViewModel? FindRestVm()
    {
        return (Ui.Main?.DataContext as ViewModels.MainViewModel)?.GetRestViewModel();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        => DeleteButton.IsEnabled = HistoryGrid.SelectedItem is not null;

    private void OnRowDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (HistoryGrid.SelectedItem is CommandHistoryItem item)
        {
            _restVm?.LoadFromHistory(item);
            Close();
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not CommandHistoryItem item) return;
        if (!Ui.Confirm(this, Localization.L("config.delete.prompt"))) return;
        App.HistoryService.Delete(item.Id);
        var vm = (RestHistoryViewModel)DataContext;
        vm.Reload();
        DeleteButton.IsEnabled = HistoryGrid.SelectedItem is not null;
    }
}
