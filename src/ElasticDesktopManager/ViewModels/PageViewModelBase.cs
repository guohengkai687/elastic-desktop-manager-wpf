using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.Services;

namespace ElasticDesktopManager.ViewModels;

/// <summary>页面 VM 基类：连接状态、加载、错误、刷新命令。</summary>
public abstract class PageViewModelBase : ObservableObject, IReloadablePage
{
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        protected set => SetProperty(ref _isLoading, value);
    }

    private bool _hasConnection = EsSession.Instance.IsConnected;
    /// <summary>当前是否已连接。带变更通知——EmptyStateView 依赖它切换空态与内容。</summary>
    public bool HasConnection
    {
        get => _hasConnection;
        private set => SetProperty(ref _hasConnection, value);
    }

    public string NotConnectedTitle => Localization.L("home.notConnected");
    public string NotConnectedHint => Localization.L("home.notConnected.hint");

    public ICommand ReloadCommand { get; }
    public ICommand OpenConnectionsCommand { get; }
    public ICommand GoLoginCommand { get; }

    protected PageViewModelBase()
    {
        ReloadCommand = new AsyncRelayCommand(_ => ReloadAsync());
        OpenConnectionsCommand = new RelayCommand(_ => Ui.ShowConnections());
        GoLoginCommand = new RelayCommand(_ => Ui.ShowConnections());
    }

    /// <summary>
    /// 连接建立/断开时由 MainViewModel 调用：同步 HasConnection（触发空态切换）
    /// 并在已连接时自动刷新一次页面数据。
    /// </summary>
    public async Task OnConnectionChangedAsync()
    {
        HasConnection = EsSession.Instance.IsConnected;
        OnPropertyChanged(nameof(NotConnectedTitle));
        OnPropertyChanged(nameof(NotConnectedHint));
        if (HasConnection)
            await AutoReloadAsync();
    }

    /// <summary>连接未建立时提示并返回 false。</summary>
    protected bool RequireConnection()
    {
        if (HasConnection) return true;
        Ui.Toast(Localization.L("common.connectionLost"));
        return false;
    }

    protected EsClient Client => EsSession.Instance.Current
        ?? throw new EsException(Localization.L("common.connectionLost"));

    public abstract Task ReloadAsync();

    /// <summary>
    /// 自动刷新入口（进入页面 / 连接建立时调用）。
    /// 默认即 ReloadAsync；子类可覆写成“静默 + 不占忙碌条”，
    /// 避免多页同时刷新时弹出一堆模态错误框并互相清掉全局忙碌状态。
    /// </summary>
    public virtual Task AutoReloadAsync() => ReloadAsync();

    /// <summary>统一执行异步加载并处理错误。</summary>
    protected async Task RunAsync(Func<Task> work, bool busy = true, bool silent = false)
    {
        if (busy) Ui.SetBusy(true);
        IsLoading = true;
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            if (!silent)
                Ui.Error(null, ex is EsException e ? e.Message : ex.Message);
        }
        finally
        {
            IsLoading = false;
            if (busy) Ui.SetBusy(false);
        }
    }
}