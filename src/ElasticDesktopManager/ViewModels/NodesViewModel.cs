using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Mvvm;

namespace ElasticDesktopManager.ViewModels;

public class NodesViewModel : PageViewModelBase
{
    public ObservableList<EsNode> Nodes { get; } = new();

    private string _filterText = "";
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
                ApplyFilter();
        }
    }

    private string _summary = "";
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    private List<EsNode> _all = new();

    public override Task ReloadAsync() => LoadAsync(busy: true, silent: false);

    /// <summary>进入页面时自动刷新：不占全局忙碌条、失败不弹模态框。</summary>
    public override Task AutoReloadAsync() => LoadAsync(busy: false, silent: true);

    private async Task LoadAsync(bool busy, bool silent)
    {
        if (!HasConnection) return;
        await RunAsync(async () =>
        {
            string json = await Client.GetNodesAsync();
            _all = EsParsers.ParseNodes(json);
            ApplyFilter();
            Summary = $"{Localization.L("node.summary")}：{_all.Count}";
        }, busy, silent);
    }

    private void ApplyFilter()
    {
        string f = FilterText?.Trim().ToLowerInvariant() ?? "";
        var filtered = string.IsNullOrEmpty(f)
            ? _all
            : _all.Where(n => n.Name.ToLowerInvariant().Contains(f)
                              || n.Ip.ToLowerInvariant().Contains(f)
                              || n.Version.ToLowerInvariant().Contains(f)).ToList();
        Nodes.ReplaceAll(filtered);
    }
}