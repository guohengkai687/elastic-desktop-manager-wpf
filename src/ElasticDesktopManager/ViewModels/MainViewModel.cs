using System.Windows.Controls;
using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.Services;

namespace ElasticDesktopManager.ViewModels;

/// <summary>导航项（Title 随语言切换刷新）。</summary>
public class NavItem : ObservableObject
{
    public required string Code { get; init; }
    public required string TitleKey { get; init; }

    public string Title => Localization.L(TitleKey);

    public void RefreshTitle() => OnPropertyChanged(nameof(Title));
}

/// <summary>可重载页面（连接变更/语言切换时调用）。</summary>
public interface IReloadablePage
{
    Task ReloadAsync();
}

public class MainViewModel : ObservableObject
{
    private readonly Dictionary<string, UserControl> _pages = new();

    public ObservableList<NavItem> NavItems { get; } = new()
    {
        new NavItem { Code = "home", TitleKey = "nav.home" },
        new NavItem { Code = "nodes", TitleKey = "nav.nodes" },
        new NavItem { Code = "shards", TitleKey = "nav.shards" },
        new NavItem { Code = "indices", TitleKey = "nav.indices" },
        new NavItem { Code = "rest", TitleKey = "nav.rest" },
        new NavItem { Code = "sql", TitleKey = "nav.sql" },
        new NavItem { Code = "search", TitleKey = "nav.search" },
    };

    private NavItem? _selectedNav;
    public NavItem? SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (SetProperty(ref _selectedNav, value) && value is not null)
                Navigate(value.Code);
        }
    }

    private object? _currentPage;
    public object? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    private string _connectionLabel = "";
    public string ConnectionLabel
    {
        get => _connectionLabel;
        private set => SetProperty(ref _connectionLabel, value);
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    /// <summary>当前连接是否开启“跳过 SSL 验证”。</summary>
    private bool _sslSkipped;
    public bool SslSkipped
    {
        get => _sslSkipped;
        private set => SetProperty(ref _sslSkipped, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private bool _isDarkTheme;
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (SetProperty(ref _isDarkTheme, value))
                OnPropertyChanged(nameof(ThemeGlyph));
        }
    }

    public string ThemeGlyph => IsDarkTheme ? "☀" : "☾";

    public ICommand OpenConnectionsCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenAboutCommand { get; }

    public MainViewModel()
    {
        OpenConnectionsCommand = new RelayCommand(_ => Ui.ShowConnections());
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
        OpenSettingsCommand = new RelayCommand(_ => Ui.ShowSettings());
        OpenAboutCommand = new RelayCommand(_ => Ui.ShowAbout());

        IsDarkTheme = ThemeService.Current == "dark";
        Ui.ConnectionChanged += OnConnectionChanged;
        Localization.LanguageChanged += OnLanguageChanged;
    }

    private void ToggleTheme()
    {
        var next = IsDarkTheme ? "light" : "dark";
        ThemeService.ApplyTheme(next);
        IsDarkTheme = next == "dark";
    }

    private void OnLanguageChanged()
    {
        foreach (var item in NavItems) item.RefreshTitle();
        UpdateStatusText();
    }

    private void OnConnectionChanged()
    {
        var session = EsSession.Instance;
        var cfg = session.CurrentConfig;
        IsConnected = session.IsConnected;
        SslSkipped = cfg?.SkipSslVerify ?? false;
        ConnectionLabel = cfg is null
            ? Localization.L("nav.connection")
            : $"{cfg.Name}  ({cfg.DisplayServerUrl()})";
        UpdateStatusText();

        if (_selectedNav is not null && GetPage(_selectedNav.Code) is IReloadablePage reloadable)
            _ = reloadable.ReloadAsync();
    }

    private void UpdateStatusText()
    {
        StatusText = IsConnected
            ? $"{ConnectionLabel}   ·   {Localization.L("config.current")}"
            : Localization.L("common.connectionLost");
    }

    public void Navigate(string code)
    {
        CurrentPage = GetPage(code);
        if (CurrentPage is IReloadablePage reloadable && EsSession.Instance.IsConnected)
            _ = reloadable.ReloadAsync();
    }

    public void NavigateSearch(string indexName)
    {
        var nav = NavItems.FirstOrDefault(x => x.Code == "search");
        if (nav is not null) SelectedNav = nav;
        if (GetPage("search") is Views.SearchView sv)
            sv.PreselectIndex(indexName);
    }

    /// <summary>当前 REST 页 VM（历史窗口回填用）。</summary>
    public RestViewModel? GetRestViewModel()
        => GetPage("rest").DataContext as RestViewModel;

    private UserControl GetPage(string code)
    {
        if (_pages.TryGetValue(code, out var existing))
            return existing;

        var view = code switch
        {
            "home" => Create<Views.HealthView, ViewModels.HealthViewModel>(),
            "nodes" => Create<Views.NodesView, ViewModels.NodesViewModel>(),
            "shards" => Create<Views.ShardsView, ViewModels.ShardsViewModel>(),
            "indices" => Create<Views.IndicesView, ViewModels.IndicesViewModel>(),
            "rest" => Create<Views.RestView, ViewModels.RestViewModel>(),
            "sql" => Create<Views.SqlView, ViewModels.SqlViewModel>(),
            "search" => Create<Views.SearchView, ViewModels.SearchViewModel>(),
            _ => throw new KeyNotFoundException(code),
        };
        _pages[code] = view;
        return view;
    }

    private static UserControl Create<TView, TVm>() where TView : UserControl, new()
        where TVm : IReloadablePage, new()
    {
        var vm = new TVm();
        return new TView { DataContext = vm };
    }
}