using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            TitleText.Text = Localization.L("nav.diag");

            TabAllocation.Header = Localization.L("diag.allocation");
            TabHotThreads.Header = Localization.L("diag.hotThreads");
            TabThreadPool.Header = Localization.L("diag.threadPool");
            TabPending.Header = Localization.L("diag.pendingTasks");

            AllocIndexLabel.Text = Localization.L("diag.index");
            AllocShardLabel.Text = Localization.L("diag.shard");
            AllocPrimaryCheck.Content = Localization.L("diag.primary");
            AllocRunButton.Content = Localization.L("diag.run");
            AllocHint.Text = Localization.L("diag.allocation.hint");

            HotNodeLabel.Text = Localization.L("diag.node");
            HotRunButton.Content = Localization.L("diag.run");
        };
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (DataContext is DiagnosticsViewModel vm)
            await vm.ReloadAsync();
    }
}
