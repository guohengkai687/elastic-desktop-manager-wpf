using System.Windows;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Services;
using ElasticDesktopManager.Services;

namespace ElasticDesktopManager;

public partial class App : Application
{
    public static SettingService SettingsService { get; } = new();
    public static ConfigService ConfigService { get; } = new();
    public static CommandHistoryService HistoryService { get; } = new();

    public static Core.Models.SettingProperty Settings => _settings.Value;

    private static readonly Lazy<Core.Models.SettingProperty> _settings = new(SettingsService.Load);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        Localization.SetLanguage(Settings.Language);
        ThemeService.ApplyTheme(Settings.Theme);

        var main = new MainWindow();
        MainWindow = main;
        main.Show();

        if (Settings.OpenDialog)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, OpenConnections);
        }
    }

    public static void OpenConnections()
    {
        Ui.ShowConnections();
    }
}