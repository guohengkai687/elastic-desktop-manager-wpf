using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.Services;

namespace ElasticDesktopManager.ViewModels;

public class SettingsViewModel : ObservableObject
{
    public List<LanguageOption> Languages { get; } = new()
    {
        new("zh_CN", "简体中文"),
        new("en", "English"),
    };

    public List<ThemeOption> Themes { get; } = new()
    {
        new("light", ""),
        new("dark", ""),
    };

    public List<CloseOption> CloseOptions { get; } = new()
    {
        new("ask", ""),
        new("minimize", ""),
        new("exit", ""),
    };

    private string _language;
    public string Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }

    private string _theme;
    public string Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, value);
    }

    private string _closeBehavior;
    public string CloseBehavior
    {
        get => _closeBehavior;
        set => SetProperty(ref _closeBehavior, value);
    }

    private int _timeout;
    public int Timeout
    {
        get => _timeout;
        set => SetProperty(ref _timeout, value);
    }

    private bool _autoTheme;
    public bool AutoTheme
    {
        get => _autoTheme;
        set => SetProperty(ref _autoTheme, value);
    }

    private bool _openDialog;
    public bool OpenDialog
    {
        get => _openDialog;
        set => SetProperty(ref _openDialog, value);
    }

    private bool _closeRemember;
    public bool CloseRemember
    {
        get => _closeRemember;
        set => SetProperty(ref _closeRemember, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public SettingsViewModel(Action? onSave = null)
    {
        var s = App.Settings;
        _language = s.Language;
        _theme = s.Theme;
        _closeBehavior = s.CloseBehavior is "minimize" or "exit" or "ask" ? s.CloseBehavior : "ask";
        _timeout = s.Timeout;
        _autoTheme = s.AutoTheme;
        _openDialog = s.OpenDialog;
        _closeRemember = s.CloseRemember;

        SaveCommand = new RelayCommand(_ =>
        {
            var s2 = App.Settings;
            s2.Language = Language;
            s2.Theme = Theme;
            s2.Timeout = Math.Clamp(Timeout, 1, 600);
            s2.AutoTheme = AutoTheme;
            s2.OpenDialog = OpenDialog;
            s2.CloseBehavior = CloseBehavior;
            s2.CloseRemember = CloseRemember;
            App.SettingsService.Save(s2);

            Localization.SetLanguage(Language);
            if (AutoTheme)
            {
                string osTheme = SystemTheme.GetWindowsTheme();
                ThemeService.ApplyTheme(osTheme);
            }
            else
            {
                ThemeService.ApplyTheme(Theme);
            }
            Ui.Toast(Localization.L("setting.saved"));
            onSave?.Invoke();
        });

        CancelCommand = new RelayCommand(_ => { });
    }
}

public record LanguageOption(string Code, string Label);
public record ThemeOption(string Code, string Label);
public record CloseOption(string Code, string Label);