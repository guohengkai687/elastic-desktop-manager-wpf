using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Mvvm;

namespace ElasticDesktopManager.ViewModels;

/// <summary>指标行（展示用）。</summary>
public class MetricRowVm
{
    public string Key { get; init; } = "";
    public string MetricValue { get; init; } = "";
    public string Node { get; init; } = "";
}

/// <summary>指标分组（可折叠）。标题按 metrics.group.&lt;key&gt; 取词条，取不到则显示原始分组名。</summary>
public class MetricGroupVm : ObservableObject
{
    public string GroupKey { get; init; } = "";

    public ObservableList<MetricRowVm> Rows { get; } = new();

    /// <summary>分组标题：优先本地化词条，缺失时回退原始 key（不显示成乱码 key 前缀）。</summary>
    public string Title
    {
        get
        {
            var key = $"metrics.group.{GroupKey}";
            var text = Localization.L(key);
            return text == key ? GroupKey : text;
        }
    }

    public string CountText => Rows.Count.ToString();

    private bool _isExpanded = true;
    /// <summary>可写：Expander 双向绑定需要 public set（只读属性绑 TwoWay 会在运行时抛异常）。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public void RefreshTitle()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CountText));
    }
}

/// <summary>指标页：_nodes/stats 扁平化后按前缀分组展示。</summary>
public class MetricsViewModel : PageViewModelBase
{
    public ObservableList<MetricGroupVm> Groups { get; } = new();

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

    private List<MetricGroupVm> _all = new();

    /// <summary>当前展开状态在刷新后保留，避免用户折叠的分组又弹开。</summary>
    private readonly Dictionary<string, bool> _expandedState = new();

    public override Task ReloadAsync() => LoadAsync(busy: true, silent: false);

    public override Task AutoReloadAsync() => LoadAsync(busy: false, silent: true);

    private async Task LoadAsync(bool busy, bool silent)
    {
        if (!HasConnection) return;
        await RunAsync(async () =>
        {
            string json = await Client.GetNodeStatsAsync();
            var rows = EsMetricsFlattener.FlattenNodeStats(json);

            _all = rows
                .GroupBy(r => r.Group)
                .Select(g =>
                {
                    var vm = new MetricGroupVm
                    {
                        GroupKey = g.Key,
                        // 默认折叠：单节点 _nodes/stats 常有数百行，全部展开会实例化大量可视元素。
                        // 用户展开过的分组在刷新后保留其状态。
                        IsExpanded = _expandedState.TryGetValue(g.Key, out var e) && e,
                    };
                    foreach (var r in g)
                        vm.Rows.Add(new MetricRowVm { Key = r.Key, MetricValue = r.Value, Node = r.Node ?? "" });
                    return vm;
                })
                .ToList();

            // 首次进入展开第一个分组，避免页面看起来是空的
            if (_expandedState.Count == 0 && _all.Count > 0)
                _all[0].IsExpanded = true;

            ApplyFilter();
            Summary = $"{Localization.L("metrics.summary")}：{rows.Count}";
        }, busy, silent);
    }

    private void ApplyFilter()
    {
        // 记住当前展开状态
        foreach (var g in _all) _expandedState[g.GroupKey] = g.IsExpanded;

        string f = FilterText?.Trim() ?? "";
        var shown = new List<MetricGroupVm>();

        foreach (var g in _all)
        {
            if (string.IsNullOrEmpty(f))
            {
                shown.Add(g);
                continue;
            }

            var matched = g.Rows
                .Where(r => r.Key.Contains(f, StringComparison.OrdinalIgnoreCase)
                            || r.MetricValue.Contains(f, StringComparison.OrdinalIgnoreCase)
                            || r.Node.Contains(f, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matched.Count == 0) continue;

            var copy = new MetricGroupVm { GroupKey = g.GroupKey, IsExpanded = true };
            foreach (var r in matched) copy.Rows.Add(r);
            shown.Add(copy);
        }

        Groups.ReplaceAll(shown);
    }

    /// <summary>语言切换时刷新分组标题。</summary>
    public void RefreshTitles()
    {
        foreach (var g in Groups) g.RefreshTitle();
    }
}
