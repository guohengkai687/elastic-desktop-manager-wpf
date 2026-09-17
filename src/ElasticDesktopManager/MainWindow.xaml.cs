using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using ElasticDesktopManager.Services;
using ElasticDesktopManager.ViewModels;
using ElasticDesktopManager.Views;

namespace ElasticDesktopManager;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Ui.Main = this;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 默认显示首页
        if (DataContext is MainViewModel vm && vm.SelectedNav is null)
            vm.SelectedNav = vm.NavItems[0];
    }

    public void NavigateTo(string pageCode)
    {
        if (DataContext is MainViewModel vm)
            vm.Navigate(pageCode);
    }

    public void NavigateSearch(string indexName)
    {
        if (DataContext is MainViewModel vm)
            vm.NavigateSearch(indexName);
    }

    public void SetBusy(bool busy)
    {
        if (DataContext is MainViewModel vm)
            vm.IsBusy = busy;
    }

    /// <summary>右下角轻提示，3 秒后淡出。</summary>
    public void ShowToast(string message)
    {
        ToastText.Text = message;
        ToastHost.Visibility = Visibility.Visible;
        ToastHost.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(400));
            fade.Completed += (_, _) => ToastHost.Visibility = Visibility.Collapsed;
            ToastHost.BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        var settings = App.Settings;

        string behavior = settings.CloseBehavior;
        if (!string.IsNullOrEmpty(behavior) && settings.CloseRemember && behavior != "ask")
        {
            e.Cancel = behavior != "exit";
            if (e.Cancel) WindowState = WindowState.Minimized;
            return;
        }

        // 询问
        var dialog = new ExitConfirmWindow { Owner = this };
        bool? result = dialog.ShowDialog();
        if (result != true)
        {
            e.Cancel = true;
            return;
        }

        string choice = dialog.Minimize ? "minimize" : "exit";
        settings.CloseBehavior = choice;
        if (dialog.Remember)
            settings.CloseRemember = true;
        App.SettingsService.Save(settings);

        if (choice != "exit")
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
        }
    }
}