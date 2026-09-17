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

    public override async Task ReloadAsync()
    {
        if (!HasConnection) return;
        await RunAsync(async () =>
        {
            string json = await Client.GetShardsAsync();
            var list = EsParsers.ParseShards(json);
            Shards.ReplaceAll(list);

            Summary = $"{Localization.L("shard.summary")}：{list.Count}";
            var counts = list.GroupBy(x => x.State)
                .Select(g => $"{g.Key}: {g.Count()}");
            StateCounts = string.Join("  ·  ", counts);
        });
    }
}