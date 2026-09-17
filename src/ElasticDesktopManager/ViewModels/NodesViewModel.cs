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

    /// <summary>是否成功加载过：没加载过就别在切语言时凭空显示"节点统计：0"。</summary>
    private bool _hasData;

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
            _hasData = true;
            ApplyFilter();
            UpdateSummary();
        }, busy, silent);
    }

    /// <summary>N 是在加载时数出来的，语言切换必须按当前语言重拼（不能只留旧语言的字符串）。</summary>
    private void UpdateSummary()
    {
        if (!_hasData) return;
        Summary = Localization.L("node.summary", _all.Count);
    }

    protected override void OnRelocalize() => UpdateSummary();

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