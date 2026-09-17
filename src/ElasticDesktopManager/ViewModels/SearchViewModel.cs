using System.Text.Json;
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

    public SearchViewModel()
    {
        RunCommand = new AsyncRelayCommand(_ => RunSearchAsync());
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
    }

    public override Task ReloadAsync() => LoadIndicesAsync(busy: true, silent: false);

    /// <summary>进入页面时自动刷新索引下拉：不占全局忙碌条、失败不弹模态框。</summary>
    public override Task AutoReloadAsync() => LoadIndicesAsync(busy: false, silent: true);

    private async Task LoadIndicesAsync(bool busy, bool silent)
    {
        if (!HasConnection) return;
        await RunAsync(async () =>
        {
            string json = await Client.GetIndicesAsync("/_cat/indices?format=json&h=index");
            var names = new List<string>();
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var name = JsonHelper.GetString(el, "index");
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            Indices.ReplaceAll(names.OrderBy(x => x, StringComparer.Ordinal));

            if (string.IsNullOrEmpty(SelectedIndex) && Indices.Count > 0)
                SelectedIndex = Indices[0];
        }, busy, silent);
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
        return JsonHelper.Serialize(root);
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

    private async Task RunSearchAsync()
    {
        if (!RequireConnection()) return;
        if (string.IsNullOrEmpty(SelectedIndex))
        {
            Ui.Error(null, Localization.L("validate.required", Localization.L("search.index")));
            return;
        }

        await RunAsync(async () =>
        {
            string body = BuildDsl();
            string raw = await Client.SearchByIndexAsync(SelectedIndex, body, TimeoutSec);
            RawJson = JsonHelper.Pretty(raw);

            var parsed = EsParsers.ParseSearchResult(raw);
            SummaryText = $"{Localization.L("search.totalHits")}: {parsed.TotalHits}  ·  {Localization.L("search.took")}: {parsed.Took}ms";

            BuildTable(parsed);
        });
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
        StructureChanged?.Invoke();
    }
}