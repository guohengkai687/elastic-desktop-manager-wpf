using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Mvvm;

namespace ElasticDesktopManager.ViewModels;

public class ShardsViewModel : PageViewModelBase
{
    public ObservableList<EsShard> Shards { get; } = new();

    private string _summary = "";
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>分片状态计数（如 STARTED / UNASSIGNED / INITIALIZING）。</summary>
    private string _stateCounts = "";
    public string StateCounts
    {
        get => _stateCounts;
        private set => SetProperty(ref _stateCounts, value);
    }

    private int _total;

    /// <summary>是否成功加载过：没加载过就别在切语言时凭空显示"分片统计：0"。</summary>
    private bool _hasData;

    public override Task ReloadAsync() => LoadAsync(busy: true, silent: false);

    /// <summary>进入页面时自动刷新：不占全局忙碌条、失败不弹模态框。</summary>
    public override Task AutoReloadAsync() => LoadAsync(busy: false, silent: true);

    private async Task LoadAsync(bool busy, bool silent)
    {
        if (!HasConnection) return;
        await RunAsync(async () =>
        {
            string json = await Client.GetShardsAsync();
            var list = EsParsers.ParseShards(json);
            Shards.ReplaceAll(list);

            _total = list.Count;
            _hasData = true;
            UpdateSummary();
            var counts = list.GroupBy(x => x.State)
                .Select(g => $"{g.Key}: {g.Count()}");
            StateCounts = string.Join("  ·  ", counts);
        }, busy, silent);
    }

    /// <summary>分片总数在加载时数出来，语言切换必须按当前语言重拼。</summary>
    private void UpdateSummary()
    {
        if (!_hasData) return;
        Summary = Localization.L("shard.summary", _total);
    }

    protected override void OnRelocalize() => UpdateSummary();
}