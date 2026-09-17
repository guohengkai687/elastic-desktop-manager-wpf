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

    public bool HasConnection => EsSession.Instance.IsConnected;

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

    /// <summary>统一执行异步加载并处理错误。</summary>
    protected async Task RunAsync(Func<Task> work, bool busy = true)
    {
        if (busy) Ui.SetBusy(true);
        IsLoading = true;
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Ui.Error(null, ex is EsException e ? e.Message : ex.Message);
        }
        finally
        {
            IsLoading = false;
            if (busy) Ui.SetBusy(false);
        }
    }
}