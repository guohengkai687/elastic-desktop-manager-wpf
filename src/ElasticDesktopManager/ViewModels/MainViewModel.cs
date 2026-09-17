using System.Windows.Controls;
using System.Windows.Media;
using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Ui;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.Services;

namespace ElasticDesktopManager.ViewModels;

/// <summary>导航项（Title 随语言切换刷新）。</summary>
public class NavItem : ObservableObject
{
    public required string Code { get; init; }
    public required string TitleKey { get; init; }

    /// <summary>矢量图标路径（24×24 视图框，描边式）。见 <see cref="AppIcons"/>。</summary>
    public string IconPath { get; init; } = "";

    /// <summary>非空表示这是一个分组标题（不是页面，不可选中）。</summary>
    public string? GroupKey { get; init; }

    public bool IsGroup => GroupKey is not null;

    /// <summary>分组项不可选、不可点。</summary>
    public bool IsSelectable => !IsGroup;

    public string Title => Localization.L(TitleKey);

    private Geometry? _iconGeometry;

    /// <summary>解析后的图标几何（惰性、只解析一次）。</summary>
    public Geometry IconGeometry
    {
        get
        {
            if (_iconGeometry is not null) return _iconGeometry;
            if (string.IsNullOrEmpty(IconPath)) return _iconGeometry = Geometry.Empty;
            try
            {
                return _iconGeometry = Geometry.Parse(IconPath);
            }
            catch (FormatException)
            {
                // 图标数据坏掉不该让整个应用起不来；测试会逐条校验 AppIcons 的语法
                return _iconGeometry = Geometry.Empty;
            }
        }
    }

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
        new NavItem { Code = "", TitleKey = "nav.group.overview", GroupKey = "g1" },
        new NavItem { Code = "home", TitleKey = "nav.home", IconPath = AppIcons.Home },

        new NavItem { Code = "", TitleKey = "nav.group.cluster", GroupKey = "g2" },
        new NavItem { Code = "nodes", TitleKey = "nav.nodes", IconPath = AppIcons.Nodes },
        new NavItem { Code = "shards", TitleKey = "nav.shards", IconPath = AppIcons.Shards },
        new NavItem { Code = "indices", TitleKey = "nav.indices", IconPath = AppIcons.Indices },
        new NavItem { Code = "metrics", TitleKey = "nav.metrics", IconPath = AppIcons.Metrics },

        new NavItem { Code = "", TitleKey = "nav.group.data", GroupKey = "g3" },
        new NavItem { Code = "search", TitleKey = "nav.search", IconPath = AppIcons.Search },
        new NavItem { Code = "snapshot", TitleKey = "nav.snapshot", IconPath = AppIcons.Snapshot },

        new NavItem { Code = "", TitleKey = "nav.group.tools", GroupKey = "g4" },
        new NavItem { Code = "rest", TitleKey = "nav.rest", IconPath = AppIcons.Rest },
        new NavItem { Code = "sql", TitleKey = "nav.sql", IconPath = AppIcons.Sql },
    };

    /// <summary>第一个真实页面（跳过分组标题）：窗口加载后默认选中。</summary>
    public NavItem? FirstPage => NavItems.FirstOrDefault(x => !x.IsGroup);

    private NavItem? _selectedNav;
    public NavItem? SelectedNav
    {
        get => _selectedNav;
        set
        {
            // 分组标题不是页面：不接受选中（否则 Navigate 会拿空 code 查表抛异常）
            if (value is { IsGroup: true }) return;
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
        ThemeService.ThemeChanged += OnThemeChanged;
        Ui.ConnectionChanged += OnConnectionChanged;
        Localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnThemeChanged() => IsDarkTheme = ThemeService.Current == "dark";

    private void ToggleTheme()
    {
        // 手动切换视为放弃“跟随系统”，否则系统偏好事件会立刻把主题改回去
        var settings = App.Settings;
        if (settings.AutoTheme)
        {
            settings.AutoTheme = false;
            App.SettingsService.Save(settings);
        }

        ThemeService.ApplyTheme(IsDarkTheme ? "light" : "dark");
        IsDarkTheme = ThemeService.Current == "dark";
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

        // 已创建的页面都要同步连接状态（空态切换）并在已连接时刷新一次数据。
        // 之前只刷新“当前页”，导致其余已缓存页面停留在旧状态（如首页仍显示未连接、搜索页索引下拉为空）。
        foreach (var page in _pages.Values)
        {
            if (page.DataContext is PageViewModelBase vm)
                _ = vm.OnConnectionChangedAsync();
        }    }

    private void UpdateStatusText()
    {
        StatusText = IsConnected
            ? $"{ConnectionLabel}   ·   {Localization.L("config.current")}"
            : Localization.L("common.connectionLost");
    }

    public void Navigate(string code)
    {
        CurrentPage = GetPage(code);

        // 进入页面即自动刷新一次（节点/分片/索引等），未连接时由各页自行跳过。
        if (CurrentPage is PageViewModelBase vm)
        {
            _ = vm.AutoReloadAsync();
        }
        else if (CurrentPage is IReloadablePage reloadable && EsSession.Instance.IsConnected)
        {
            _ = reloadable.ReloadAsync();
        }
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
            "metrics" => Create<Views.MetricsView, ViewModels.MetricsViewModel>(),
            "rest" => Create<Views.RestView, ViewModels.RestViewModel>(),
            "sql" => Create<Views.SqlView, ViewModels.SqlViewModel>(),
            "search" => Create<Views.SearchView, ViewModels.SearchViewModel>(),
            "snapshot" => Create<Views.SnapshotView, ViewModels.SnapshotViewModel>(),
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