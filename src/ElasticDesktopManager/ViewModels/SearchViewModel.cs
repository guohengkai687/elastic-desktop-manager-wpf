using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Json;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.Services;
using ElasticDesktopManager.Views;

namespace ElasticDesktopManager.ViewModels;

public record OperatorOption(string Code, string Label);
public record ClauseOption(string Code, string Label);

/// <summary>每页条数下拉项。文案（"10 条/页"）随语言变化，因此由 VM 在语言切换时重建。</summary>
public record PageSizeOption(int Value, string Label);

/// <summary>查询构建条件行。</summary>
public class QueryCondition : ObservableObject
{
    private string _clause = "must";
    private string _field = "";
    private string _operator = "term";
    private string _value = "";

    public string Clause { get => _clause; set => SetProperty(ref _clause, value); }
    public string Field { get => _field; set => SetProperty(ref _field, value); }
    public string Operator { get => _operator; set => SetProperty(ref _operator, value); }
    public string Value { get => _value; set => SetProperty(ref _value, value); }
}

public class SearchViewModel : PageViewModelBase
{
    public List<OperatorOption> Operators { get; } = new()
    {
        new("term", "term"),
        new("match", "match"),
        new("wildcard", "wildcard"),
        new("prefix", "prefix"),
        new("range", "range"),
        new("exists", "exists"),
    };

    public List<ClauseOption> Clauses { get; } = new()
    {
        new("must", "must"),
        new("should", "should"),
        new("must_not", "must_not"),
        new("filter", "filter"),
    };

    public ObservableList<string> Indices { get; } = new();
    public ObservableList<QueryCondition> Conditions { get; } = new();

    private string _selectedIndex = "";
    public string SelectedIndex
    {
        get => _selectedIndex;
        set => SetProperty(ref _selectedIndex, value);
    }

    /// <summary>索引下拉右侧的状态：加载成功显示数量，失败显示原因（不再静默）。</summary>
    private string _indexHint = "";
    public string IndexHint
    {
        get => _indexHint;
        private set => SetProperty(ref _indexHint, value);
    }

    private int _timeoutSec = 30;
    public int TimeoutSec
    {
        get => _timeoutSec;
        set => SetProperty(ref _timeoutSec, value);
    }

    private bool _trackTotalHits = true;
    public bool TrackTotalHits
    {
        get => _trackTotalHits;
        set => SetProperty(ref _trackTotalHits, value);
    }

    /// <summary>Painless 更新脚本（“按查询更新”必需，否则 ES 不会改动文档）。</summary>
    private string _updateScript = "";
    public string UpdateScript
    {
        get => _updateScript;
        set => SetProperty(ref _updateScript, value);
    }

    public ObservableList<Dictionary<string, string>> Rows { get; } = new();
    public List<string> Columns { get; private set; } = new();

    // ==================== 服务端分页 ====================
    //
    // ES 的 _search 默认只返回 10 条，"翻页"必须由服务端完成：把 from/size 写进 DSL。
    // 客户端无法从这 10 条里翻出其余命中（这正是"总命中 2570 却只有 10 行"的原因）。
    // 行为对齐 JavaFX 原版的 PagingControl + getQueryConditionsParms。

    private int _pageSize = SearchPaging.DefaultPageSize;
    private int _pageNum = 1;
    private long _totalHits;
    private long _took;
    private bool _totalHitsIsLowerBound;
    private bool _hasSearched;
    private bool _clampRetry;   // "页码收敛后重查一次"的递归闸门
    private int _searchGeneration;   // 请求代次：丢弃被更新查询取代的迟到响应

    /// <summary>每页条数：改变时回到第 1 页并重查（与原版一致）。</summary>
    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (!SetProperty(ref _pageSize, value <= 0 ? SearchPaging.DefaultPageSize : value)) return;
            // 换每页条数后旧页码没有意义；回到第 1 页同时保证 from=0 必然在结果窗口内。
            // 这里直接改字段 + 统一补通知（而不是走 PageNum setter）：PageNum 本来就是 1 时
            // setter 会提前返回，但 TotalPages/PageInfoText 仍然依赖 PageSize 变化，必须刷新。
            _pageNum = 1;
            RaisePagingChanged();
            if (_hasSearched) _ = RunSearchAsync();
        }
    }

    /// <summary>当前页号（从 1 开始）。只由翻页命令 / 查询结果收敛修改。</summary>
    public int PageNum
    {
        get => _pageNum;
        private set
        {
            if (!SetProperty(ref _pageNum, value)) return;
            RaisePagingChanged();
        }
    }

    public int TotalPages => SearchPaging.TotalPages(_totalHits, _pageSize);

    /// <summary>查询过之后才显示分页条（没查过时显示"共 0 条"没有意义）。</summary>
    public bool PagingVisible => _hasSearched;

    public bool CanGoPrev => _hasSearched && _pageNum > 1;

    public bool CanGoNext => _hasSearched
        && SearchPaging.HasNext(_pageNum, _pageSize, _totalHits, _totalHitsIsLowerBound);

    /// <summary>命中总数文案；relation=gte 时是下限，必须显示成 "10000+"。</summary>
    public string TotalHitsText => Localization.L(
        _totalHitsIsLowerBound ? "search.page.totalLower" : "search.page.total", _totalHits);

    public string PageInfoText => Localization.L("search.page.info", _pageNum, TotalPages);

    /// <summary>每页条数下拉项（文案本地化，语言切换时由 <see cref="Relocalize"/> 重建）。</summary>
    public List<PageSizeOption> PageSizeOptions { get; private set; } = BuildPageSizeOptions();

    private static List<PageSizeOption> BuildPageSizeOptions() =>
        SearchPaging.PageSizes
            .Select(s => new PageSizeOption(s, Localization.L("search.page.sizeSuffix", s)))
            .ToList();

    /// <summary>"前往 N 页"输入框内容（按回车或点按钮生效）。</summary>
    private string _goToPageText = "";
    public string GoToPageText
    {
        get => _goToPageText;
        set => SetProperty(ref _goToPageText, value);
    }

    private string _rawJson = "";
    public string RawJson
    {
        get => _rawJson;
        private set => SetProperty(ref _rawJson, value);
    }

    private string _summaryText = "";
    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public event Action? StructureChanged;

    public AsyncRelayCommand RunCommand { get; }
    public ICommand ShowDslCommand { get; }
    public ICommand AddConditionCommand { get; }
    public ICommand RemoveConditionCommand { get; }
    public AsyncRelayCommand DeleteByQueryCommand { get; }
    public AsyncRelayCommand UpdateByQueryCommand { get; }
    public ICommand ClearResultsCommand { get; }

    // 分页命令。首/上/下/末用 RelayCommand + CanExecute：WPF 的 CommandManager 会在交互后重查，
    // 按钮能自动置灰，不需要手工 RaiseCanExecuteChanged。
    public ICommand FirstPageCommand { get; }
    public ICommand PrevPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand LastPageCommand { get; }
    public AsyncRelayCommand GoToPageCommand { get; }

    /// <summary>手动重载索引下拉（失败时弹错误框，便于排查为什么列表是空的）。</summary>
    public AsyncRelayCommand RefreshIndicesCommand { get; }

    public SearchViewModel()
    {
        // 点"搜索"= 一次新查询，回到第 1 页；翻页与换每页条数不走这里。
        RunCommand = new AsyncRelayCommand(_ => RunSearchAsync(resetPage: true));
        ShowDslCommand = new RelayCommand(_ => ShowDsl());
        AddConditionCommand = new RelayCommand(_ =>
            Conditions.Add(new QueryCondition { Clause = "must", Operator = "term" }));
        RemoveConditionCommand = new RelayCommand(p =>
        {
            if (p is QueryCondition c) Conditions.Remove(c);
        });
        DeleteByQueryCommand = new AsyncRelayCommand(_ => ModifyByQueryAsync(isUpdate: false));
        UpdateByQueryCommand = new AsyncRelayCommand(_ => ModifyByQueryAsync(isUpdate: true));
        ClearResultsCommand = new RelayCommand(_ => ClearResults());
        RefreshIndicesCommand = new AsyncRelayCommand(_ => LoadIndicesAsync(busy: false, silent: false));

        FirstPageCommand = new RelayCommand(_ => _ = GoToPageAsync(1), _ => CanGoPrev);
        PrevPageCommand = new RelayCommand(_ => _ = GoToPageAsync(PageNum - 1), _ => CanGoPrev);
        NextPageCommand = new RelayCommand(_ => _ = GoToPageAsync(PageNum + 1), _ => CanGoNext);
        LastPageCommand = new RelayCommand(_ => _ = GoToPageAsync(TotalPages),
            _ => _hasSearched && _pageNum < TotalPages);
        GoToPageCommand = new AsyncRelayCommand(_ => GoToPageFromTextAsync());
    }

    public override Task ReloadAsync() => LoadIndicesAsync(busy: true, silent: false);

    /// <summary>进入页面时自动刷新索引下拉：不占全局忙碌条、失败不弹模态框。</summary>
    public override Task AutoReloadAsync() => LoadIndicesAsync(busy: false, silent: true);

    private async Task LoadIndicesAsync(bool busy, bool silent)
    {
        if (!HasConnection)
        {
            IndexHint = "";
            return;
        }

        if (busy) Ui.SetBusy(true);
        IsLoading = true;
        try
        {
            string json = await Client.GetIndicesAsync(EsClient.IndexNamesFormat);
            var names = EsParsers.ParseIndexNames(json);
            Indices.ReplaceAll(names.OrderBy(x => x, StringComparer.Ordinal));

            if (string.IsNullOrEmpty(SelectedIndex) && Indices.Count > 0)
                SelectedIndex = Indices[0];

            // 0 个索引也要说清楚：是集群里真的没有索引，而不是"没加载出来"
            IndexHint = Indices.Count > 0
                ? Localization.L("search.index.count", Indices.Count)
                : Localization.L("search.index.empty");
        }
        catch (Exception ex)
        {
            // 静默模式也必须把原因显示在页面上：此前失败被完全吞掉，
            // 表现为"下拉是空的、也没有任何提示"，用户无法判断是没索引还是请求失败。
            IndexHint = ex is EsException e ? e.Message : ex.Message;
            if (!silent) Ui.Error(null, IndexHint);
        }
        finally
        {
            IsLoading = false;
            if (busy) Ui.SetBusy(false);
        }
    }

    public void PreselectIndex(string indexName)
    {
        if (!string.IsNullOrEmpty(indexName) && !Indices.Contains(indexName))
            Indices.Insert(0, indexName);
        SelectedIndex = indexName;
    }

    /// <summary>生成 DSL 查询体（含 track_total_hits 与 timeout）。</summary>
    public string BuildDsl(bool withOptions = true)
    {
        var root = new Dictionary<string, object?>();

        if (Conditions.Count == 0)
        {
            root["query"] = new Dictionary<string, object?> { ["match_all"] = new Dictionary<string, object?>() };
        }
        else
        {
            var boolClause = new Dictionary<string, object?>();
            foreach (var group in Conditions.GroupBy(c => c.Clause))
            {
                var arr = new List<object?>();
                foreach (var c in group)
                {
                    var cond = BuildCondition(c);
                    if (cond is not null) arr.Add(cond);
                }
                if (arr.Count > 0)
                    boolClause[group.Key] = arr;
            }

            // 空条件回退（例如全部 exists 且未填字段）
            if (boolClause.Count == 0)
                root["query"] = new Dictionary<string, object?> { ["match_all"] = new Dictionary<string, object?>() };
            else
                root["query"] = new Dictionary<string, object?> { ["bool"] = boolClause };
        }

        if (withOptions)
        {
            root["track_total_hits"] = TrackTotalHits;
            root["timeout"] = $"{Math.Max(1, TimeoutSec)}s";
        }
        string body = JsonHelper.Serialize(root);

        // 分页只在真正执行 _search 时带上：_update_by_query / _delete_by_query 不接受 from
        // （它们用 max_docs），带上会被 ES 拒绝，所以 withOptions:false 的那条路径不加。
        return withOptions
            ? EsQueryHelper.WithPaging(body, SearchPaging.FromOf(PageNum, PageSize), PageSize)
            : body;
    }

    private static object? BuildCondition(QueryCondition c)
    {
        string field = c.Field?.Trim() ?? "";
        if (string.IsNullOrEmpty(field)) return null;

        switch (c.Operator)
        {
            case "match":
                return new Dictionary<string, object?> { ["match"] = new Dictionary<string, object?> { [field] = c.Value } };
            case "wildcard":
                return Dsl("wildcard", field, c.Value);
            case "prefix":
                return Dsl("prefix", field, c.Value);
            case "term":
                return Dsl("term", field, ParseScalar(c.Value));
            case "range":
            {
                var (gte, lte) = SplitRange(c.Value);
                if (gte is null && lte is null) return null;
                var r = new Dictionary<string, object?>();
                if (gte is not null) r["gte"] = gte;
                if (lte is not null) r["lte"] = lte;
                return new Dictionary<string, object?> { ["range"] = new Dictionary<string, object?> { [field] = r } };
            }
            case "exists":
                return new Dictionary<string, object?> { ["exists"] = new Dictionary<string, object?> { ["field"] = field } };
            default:
                return Dsl("term", field, c.Value);
        }
    }

    private static Dictionary<string, object?> Dsl(string op, string field, object? value)
        => new() { [op] = new Dictionary<string, object?> { [field] = value } };

    private static object? ParseScalar(string value)
    {
        var v = value?.Trim() ?? "";
        if (long.TryParse(v, out long l)) return l;
        if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)) return d;
        if (bool.TryParse(v, out bool b)) return b;
        return v;
    }

    private static (object? gte, object? lte) SplitRange(string value)
    {
        var v = value ?? "";
        var parts = v.Split(new[] { ".." }, StringSplitOptions.None);
        if (parts.Length != 2) return (null, null);
        return (ParseScalar(parts[0].Trim()), ParseScalar(parts[1].Trim()));
    }

    private async Task RunSearchAsync(bool resetPage = false)
    {
        if (!RequireConnection()) return;
        if (string.IsNullOrEmpty(SelectedIndex))
        {
            Ui.Error(null, Localization.L("validate.required", Localization.L("search.index")));
            return;
        }
        if (resetPage) PageNum = 1;

        // 翻页/换每页条数都可能连着点，两个请求在途时先发的后到会覆盖新结果
        // （第 4 轮审查在快照页抓到过同类问题）。用代次号丢弃过期响应。
        int generation = ++_searchGeneration;

        await RunAsync(async () =>
        {
            string body = BuildDsl();
            string raw = await Client.SearchByIndexAsync(SelectedIndex, body, TimeoutSec);
            if (generation != _searchGeneration) return;   // 已被更新的查询取代：丢弃，保持界面为最新一次
            RawJson = JsonHelper.Pretty(raw);

            var parsed = EsParsers.ParseSearchResult(raw);
            ApplyResult(parsed);

            BuildTable(parsed);
        });

        // 结果集变小后当前页可能已越界（例如在第 5 页缩小条件只剩 12 条）：收敛到最后一页重查一次。
        // 原版不处理这种情况，会留下"第 5 / 2 页 + 空表"的迷惑状态。_clampRetry 保证最多重查一次。
        if (!_clampRetry && _hasSearched && PageNum > TotalPages)
        {
            _clampRetry = true;
            try { await GoToPageAsync(TotalPages); }
            finally { _clampRetry = false; }
        }
    }

    /// <summary>把一次查询结果落到分页状态与汇总文案上。</summary>
    private void ApplyResult(EsSearchResult parsed)
    {
        _totalHits = parsed.TotalHits;
        _totalHitsIsLowerBound = parsed.TotalHitsIsLowerBound;
        _took = parsed.Took;
        _hasSearched = true;
        SummaryText = BuildSummaryText();
        RaisePagingChanged();
    }

    private string BuildSummaryText()
    {
        string total = _totalHitsIsLowerBound
            ? $"{_totalHits}+"
            : _totalHits.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"{Localization.L("search.totalHits")}: {total}  ·  {Localization.L("search.took")}: {_took}ms";
    }

    /// <summary>跳到指定页（页号会收敛到 [1, 总页数]；超出 ES 结果窗口时给出可读错误而不是发一个必然 400 的请求）。</summary>
    private async Task GoToPageAsync(int page)
    {
        if (!_hasSearched || IsLoading) return;

        int target = SearchPaging.ClampPage(page, TotalPages);
        if (SearchPaging.ExceedsWindow(target, PageSize))
        {
            Ui.Error(null, Localization.L("search.page.limit",
                SearchPaging.FromOf(target, PageSize), SearchPaging.MaxFrom));
            return;
        }
        if (target == PageNum) return;

        PageNum = target;
        await RunSearchAsync();
    }

    private async Task GoToPageFromTextAsync()
    {
        string text = (GoToPageText ?? "").Trim();
        GoToPageText = "";
        if (!int.TryParse(text, out int page)) return;   // 空/非数字：静默忽略，不打扰用户
        await GoToPageAsync(page);
    }

    /// <summary>由视图在语言切换时调用：重算本 VM 拼装的动态文案（视图 chrome 由视图自己刷新）。</summary>
    public void Relocalize()
    {
        PageSizeOptions = BuildPageSizeOptions();
        OnPropertyChanged(nameof(PageSizeOptions));
        if (_hasSearched) SummaryText = BuildSummaryText();
        RaisePagingChanged();
    }

    private void RaisePagingChanged()
    {
        // PageNum 可能被直接改字段（换每页条数 / 清空结果），所以在这里统一补通知。
        OnPropertyChanged(nameof(PageNum));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PagingVisible));
        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(TotalHitsText));
        OnPropertyChanged(nameof(PageInfoText));
    }

    private void BuildTable(EsSearchResult parsed)
    {
        var columns = new List<string>();
        var rows = new List<Dictionary<string, string>>();

        foreach (var hit in parsed.Hits)
        {
            var dict = new Dictionary<string, string>();
            dict["_id"] = hit.Id;
            dict["_index"] = hit.Index;
            dict["_score"] = hit.Score.ToString("0.####");
            foreach (var kv in hit.Source)
            {
                var value = FormatValue(kv.Value);
                dict[kv.Key] = value;
                if (!columns.Contains(kv.Key))
                    columns.Add(kv.Key);
            }
            rows.Add(dict);
        }

        // _id/_index/_score 列在前
        Columns = new List<string> { "_id", "_index", "_score" }.Concat(columns).ToList();
        Rows.ReplaceAll(rows);
        StructureChanged?.Invoke();
    }

    private static string FormatValue(object? v)
    {
        return v switch
        {
            null => "",
            string s => s,
            bool b => b ? "true" : "false",
            _ => v.ToString() ?? "",
        };
    }

    private void ShowDsl()
    {
        var dlg = new JsonViewerWindow(Localization.L("search.dsl.title"), BuildDsl()) { Owner = Ui.Main };
        dlg.ShowDialog();
    }

    private async Task ModifyByQueryAsync(bool isUpdate)
    {
        if (!RequireConnection()) return;
        if (string.IsNullOrEmpty(SelectedIndex)) return;

        if (isUpdate && string.IsNullOrWhiteSpace(UpdateScript))
        {
            Ui.Info(null, Localization.L("search.update.needScript"));
            return;
        }

        string confirmKey = isUpdate ? "search.confirm.update" : "search.confirm.delete";
        if (!Ui.Confirm(null, Localization.L(confirmKey))) return;

        await RunAsync(async () =>
        {
            // 仅查询部分（不含 track_total_hits/timeout）
            string queryDsl = BuildDsl(withOptions: false);
            if (isUpdate)
            {
                string body = EsQueryHelper.BuildUpdateByQueryBody(queryDsl, UpdateScript);
                await Client.UpdateByQueryAsync(SelectedIndex, body);
            }
            else
            {
                string body = EsQueryHelper.ExtractQueryPart(queryDsl);
                await Client.DeleteByQueryAsync(SelectedIndex, body);
            }
            Ui.Toast(Localization.L("common.action.success"));
            await RunSearchAsync();
        });
    }

    private void ClearResults()
    {
        Rows.ReplaceAll(new List<Dictionary<string, string>>());
        RawJson = "";
        SummaryText = "";
        Columns = new List<string>();
        // 分页状态一并复位：否则清空后仍显示"第 3 / 8 页"，下次搜索又会带着旧页码发请求。
        _hasSearched = false;
        _totalHits = 0;
        _took = 0;
        _totalHitsIsLowerBound = false;
        _clampRetry = false;
        _pageNum = 1;
        GoToPageText = "";
        RaisePagingChanged();
        StructureChanged?.Invoke();
    }
}