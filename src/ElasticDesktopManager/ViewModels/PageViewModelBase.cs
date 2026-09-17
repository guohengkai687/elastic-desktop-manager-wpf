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

    /// <summary>
    /// 语言切换时刷新本页 VM 侧拼装的文案。
    /// <para>
    /// 由**页面视图**的语言处理器调用（视图订阅 <see cref="Localization.LanguageChanged"/> 并在此后调本方法）：
    /// 视图缓存后与应用同生命周期，而 VM 未必——索引工具窗每次打开都会新建一个 VM，
    /// 若改由 VM 订阅静态事件，关窗后 VM 会被事件永久持有（越开越漏）。所以订阅的责任留在视图侧。
    /// </para>
    /// <para>基类先刷新空态文案（<see cref="NotConnectedTitle"/>/<see cref="NotConnectedHint"/>），再交给子类重算自己的缓存文案。</para>
    /// </summary>
    public void Relocalize()
    {
        OnPropertyChanged(nameof(NotConnectedTitle));
        OnPropertyChanged(nameof(NotConnectedHint));
        OnRelocalize();
    }

    /// <summary>
    /// 子类重算自己拼装/缓存的本地化文案（如 “共 N 条” 这类在加载时拼好、之后不会再算的字符串）。
    /// 默认无操作：文案每次都现算的页面（如 REST 页）不需要重写。
    /// </summary>
    protected virtual void OnRelocalize()
    {
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