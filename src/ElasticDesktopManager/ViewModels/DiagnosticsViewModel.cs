using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Mvvm;

namespace ElasticDesktopManager.ViewModels;

/// <summary>
/// 诊断页：分片分配解释 / 热点线程 / 线程池 / 挂起任务。
/// 四块能力互相独立，各自持有输入与输出文本。
/// </summary>
public class DiagnosticsViewModel : PageViewModelBase
{
    // ---------- 1. 分片分配解释 ----------
    private string _allocIndex = "";
    public string AllocIndex
    {
        get => _allocIndex;
        set => SetProperty(ref _allocIndex, value ?? "");
    }

    private string _allocShard = "";
    public string AllocShard
    {
        get => _allocShard;
        set => SetProperty(ref _allocShard, value ?? "");
    }

    private bool _allocPrimary;
    public bool AllocPrimary
    {
        get => _allocPrimary;
        set => SetProperty(ref _allocPrimary, value);
    }

    private string _allocResult = "";
    public string AllocResult
    {
        get => _allocResult;
        private set => SetProperty(ref _allocResult, value);
    }

    // ---------- 2. 热点线程 ----------
    private string _hotNode = "";
    public string HotNode
    {
        get => _hotNode;
        set => SetProperty(ref _hotNode, value ?? "");
    }

    private string _hotResult = "";
    public string HotResult
    {
        get => _hotResult;
        private set => SetProperty(ref _hotResult, value);
    }

    // ---------- 3. 线程池 ----------
    private string _threadPoolResult = "";
    public string ThreadPoolResult
    {
        get => _threadPoolResult;
        private set => SetProperty(ref _threadPoolResult, value);
    }

    // ---------- 4. 挂起任务 ----------
    private string _pendingResult = "";
    public string PendingResult
    {
        get => _pendingResult;
        private set => SetProperty(ref _pendingResult, value);
    }

    public ICommand RunAllocationCommand { get; }
    public ICommand RunHotThreadsCommand { get; }
    public ICommand RunThreadPoolCommand { get; }
    public ICommand RunPendingCommand { get; }

    public DiagnosticsViewModel()
    {
        RunAllocationCommand = new AsyncRelayCommand(_ => RunAllocationAsync());
        RunHotThreadsCommand = new AsyncRelayCommand(_ => RunHotThreadsAsync());
        RunThreadPoolCommand = new AsyncRelayCommand(_ => RunThreadPoolAsync());
        RunPendingCommand = new AsyncRelayCommand(_ => RunPendingAsync());
    }

    /// <summary>进入页面时拉取线程池与挂起任务（只读、开销小）。</summary>
    public override Task ReloadAsync() => LoadAsync(busy: true, silent: false);

    public override Task AutoReloadAsync() => LoadAsync(busy: false, silent: true);

    private async Task LoadAsync(bool busy, bool silent)
    {
        if (!HasConnection) return;
        await RunAsync(async () =>
        {
            ThreadPoolResult = JsonHelperPretty(await Client.GetThreadPoolAsync());
            PendingResult = JsonHelperPretty(await Client.GetPendingTasksAsync());
        }, busy, silent);
    }

    private async Task RunAllocationAsync()
    {
        if (!RequireConnection()) return;
        await RunAsync(async () =>
        {
            int? shard = int.TryParse(AllocShard.Trim(), out var s) ? s : null;
            bool? primary = AllocPrimary ? true : null;
            string json = await Client.ExplainAllocationAsync(
                string.IsNullOrWhiteSpace(AllocIndex) ? null : AllocIndex.Trim(), shard, primary);
            AllocResult = JsonHelperPretty(json);
        });
    }

    private async Task RunHotThreadsAsync()
    {
        if (!RequireConnection()) return;
        await RunAsync(async () =>
        {
            // 热点线程返回的是纯文本（非 JSON），不能做 JSON 美化
            string text = await Client.HotThreadsAsync(
                string.IsNullOrWhiteSpace(HotNode) ? null : HotNode.Trim());
            HotResult = text;
        });
    }

    private async Task RunThreadPoolAsync()
    {
        if (!RequireConnection()) return;
        await RunAsync(async () =>
        {
            ThreadPoolResult = JsonHelperPretty(await Client.GetThreadPoolAsync());
        });
    }

    private async Task RunPendingAsync()
    {
        if (!RequireConnection()) return;
        await RunAsync(async () =>
        {
            PendingResult = JsonHelperPretty(await Client.GetPendingTasksAsync());
        });
    }

    /// <summary>尽力美化；非 JSON（如 es 返回错误文本）时原样返回。</summary>
    private static string JsonHelperPretty(string raw)
        => Core.Json.JsonHelper.TryPretty(raw, out var pretty) ? pretty : raw;
}
