using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.Services;

namespace ElasticDesktopManager.ViewModels;

/// <summary>
/// 索引工具（对话框）：把已实现的运维端点接到界面上——
/// Mapping / Settings 查看与更新、别名增删、字段 Top 值、Force Merge、Reindex 迁移。
/// </summary>
public class IndexToolsViewModel : PageViewModelBase
{
    public string IndexName { get; }

    public IndexToolsViewModel(string indexName)
    {
        IndexName = indexName;

        LoadMappingCommand = new AsyncRelayCommand(_ => LoadMappingAsync());
        SaveMappingCommand = new AsyncRelayCommand(_ => SaveMappingAsync());
        LoadSettingsCommand = new AsyncRelayCommand(_ => LoadSettingsAsync());
        SaveSettingsCommand = new AsyncRelayCommand(_ => SaveSettingsAsync());
        LoadAliasesCommand = new AsyncRelayCommand(_ => LoadAliasesAsync());
        AddAliasCommand = new AsyncRelayCommand(_ => AddAliasAsync());
        RemoveAliasCommand = new AsyncRelayCommand(_ => RemoveAliasAsync());
        LoadTopValuesCommand = new AsyncRelayCommand(_ => LoadTopValuesAsync());
        ForceMergeCommand = new AsyncRelayCommand(_ => ForceMergeAsync());
        ReindexCommand = new AsyncRelayCommand(_ => ReindexAsync());
    }

    // ---------- Mapping ----------
    private string _mappingJson = "";
    public string MappingJson
    {
        get => _mappingJson;
        set => SetProperty(ref _mappingJson, value ?? "");
    }

    // ---------- Settings ----------
    private string _settingsJson = "";
    public string SettingsJson
    {
        get => _settingsJson;
        set => SetProperty(ref _settingsJson, value ?? "");
    }

    // ---------- 别名 ----------
    public ObservableList<EsAlias> Aliases { get; } = new();

    private EsAlias? _selectedAlias;
    public EsAlias? SelectedAlias
    {
        get => _selectedAlias;
        set => SetProperty(ref _selectedAlias, value);
    }

    private string _newAliasName = "";
    public string NewAliasName
    {
        get => _newAliasName;
        set => SetProperty(ref _newAliasName, value ?? "");
    }

    private string _newAliasFilter = "";
    public string NewAliasFilter
    {
        get => _newAliasFilter;
        set => SetProperty(ref _newAliasFilter, value ?? "");
    }

    private string _newAliasRouting = "";
    public string NewAliasRouting
    {
        get => _newAliasRouting;
        set => SetProperty(ref _newAliasRouting, value ?? "");
    }

    // ---------- 字段 Top 值 ----------
    public ObservableList<FieldTopValue> TopValues { get; } = new();

    private string _topField = "";
    public string TopField
    {
        get => _topField;
        set => SetProperty(ref _topField, value ?? "");
    }

    private string _topSize = "20";
    public string TopSize
    {
        get => _topSize;
        set => SetProperty(ref _topSize, value ?? "");
    }

    private bool _topUseKeyword = true;
    public bool TopUseKeyword
    {
        get => _topUseKeyword;
        set => SetProperty(ref _topUseKeyword, value);
    }

    private string _topSummary = "";
    public string TopSummary
    {
        get => _topSummary;
        private set => SetProperty(ref _topSummary, value);
    }

    // ---------- Force Merge ----------
    private string _maxSegments = "1";
    public string MaxSegments
    {
        get => _maxSegments;
        set => SetProperty(ref _maxSegments, value ?? "");
    }

    // ---------- Reindex ----------
    private string _reindexTarget = "";
    public string ReindexTarget
    {
        get => _reindexTarget;
        set => SetProperty(ref _reindexTarget, value ?? "");
    }

    private string _reindexQuery = "";
    public string ReindexQuery
    {
        get => _reindexQuery;
        set => SetProperty(ref _reindexQuery, value ?? "");
    }

    public ICommand LoadMappingCommand { get; }
    public ICommand SaveMappingCommand { get; }
    public ICommand LoadSettingsCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand LoadAliasesCommand { get; }
    public ICommand AddAliasCommand { get; }
    public ICommand RemoveAliasCommand { get; }
    public ICommand LoadTopValuesCommand { get; }
    public ICommand ForceMergeCommand { get; }
    public ICommand ReindexCommand { get; }

    /// <summary>打开对话框时加载 Mapping / Settings / 别名。</summary>
    public override Task ReloadAsync() => LoadAllAsync();

    private async Task LoadAllAsync()
    {
        await RunAsync(async () =>
        {
            MappingJson = JsonHelperPretty(await Client.GetMappingAsync(IndexName));
            SettingsJson = JsonHelperPretty(await Client.GetSettingsAsync(IndexName));
            await LoadAliasesCoreAsync();
        });
    }

    private Task LoadMappingAsync() => RunAsync(async () =>
    {
        MappingJson = JsonHelperPretty(await Client.GetMappingAsync(IndexName));
    });

    private Task SaveMappingAsync() => RunAsync(async () =>
    {
        // ES 的 PUT _mapping 只接受 { "properties": {...} }，但 GET 返回外层带索引名，
        // 因此这里取出该索引对应的映射体再提交，避免用户还要手工裁剪。
        string body = EsQueryHelper.ExtractMappingBody(MappingJson);
        await Client.PutMappingAsync(IndexName, body);
        Ui.Toast(Localization.L("indextools.saved"));
        MappingJson = JsonHelperPretty(await Client.GetMappingAsync(IndexName));
    });

    private Task LoadSettingsAsync() => RunAsync(async () =>
    {
        SettingsJson = JsonHelperPretty(await Client.GetSettingsAsync(IndexName));
    });

    private Task SaveSettingsAsync() => RunAsync(async () =>
    {
        await Client.PutSettingsAsync(IndexName, EsQueryHelper.ExtractSettingsBody(SettingsJson));
        Ui.Toast(Localization.L("indextools.saved"));
        SettingsJson = JsonHelperPretty(await Client.GetSettingsAsync(IndexName));
    });

    private async Task LoadAliasesAsync()
    {
        await RunAsync(LoadAliasesCoreAsync);
    }

    private async Task LoadAliasesCoreAsync()
    {
        var list = EsParsers.ParseAliases(await Client.GetIndexAliasesAsync(IndexName));
        Aliases.ReplaceAll(list);
        if (SelectedAlias is null && list.Count > 0) SelectedAlias = list[0];
    }

    private Task AddAliasAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(NewAliasName))
        {
            Ui.Error(null, Localization.L("indextools.alias.needName"));
            return;
        }
        await Client.AddAliasAsync(IndexName, NewAliasName.Trim(),
            string.IsNullOrWhiteSpace(NewAliasFilter) ? null : NewAliasFilter,
            string.IsNullOrWhiteSpace(NewAliasRouting) ? null : NewAliasRouting);
        Ui.Toast(Localization.L("indextools.saved"));
        NewAliasName = "";
        NewAliasFilter = "";
        NewAliasRouting = "";
        await LoadAliasesCoreAsync();
    });

    private Task RemoveAliasAsync() => RunAsync(async () =>
    {
        if (SelectedAlias is null) return;
        if (!Ui.Confirm(null, Localization.L("indextools.alias.remove.prompt", SelectedAlias.Name))) return;
        await Client.RemoveAliasAsync(SelectedAlias.Index, SelectedAlias.Name);
        Ui.Toast(Localization.L("indextools.saved"));
        await LoadAliasesCoreAsync();
    });

    private Task LoadTopValuesAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(TopField))
        {
            Ui.Error(null, Localization.L("indextools.top.needField"));
            return;
        }

        int size = int.TryParse(TopSize, out var s) ? Math.Clamp(s, 1, 1000) : 20;
        string json = await Client.FieldTopValuesAsync(IndexName, TopField.Trim(), size, TopUseKeyword);
        var result = EsParsers.ParseFieldTopValues(json);

        TopValues.ReplaceAll(result.Values);
        TopSummary = result.Error is not null
            ? $"{Localization.L("indextools.top.error")}：{result.Error}"
            : $"{Localization.L("indextools.top.distinct")}：{result.DistinctCount}";
    });

    private Task ForceMergeAsync() => RunAsync(async () =>
    {
        if (!Ui.Confirm(null, Localization.L("indextools.forcemerge.prompt", IndexName))) return;
        int max = int.TryParse(MaxSegments, out var m) ? Math.Max(1, m) : 1;
        await Client.ForceMergeAsync(IndexName, max);
        Ui.Toast(Localization.L("indextools.done"));
    });

    private Task ReindexAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(ReindexTarget))
        {
            Ui.Error(null, Localization.L("indextools.reindex.needTarget"));
            return;
        }
        if (!Ui.Confirm(null, Localization.L("indextools.reindex.prompt", IndexName, ReindexTarget.Trim()))) return;

        await Client.ReindexAsync(IndexName, ReindexTarget.Trim(),
            string.IsNullOrWhiteSpace(ReindexQuery) ? null : ReindexQuery);
        Ui.Toast(Localization.L("indextools.reindex.started"));
    });

    // ---------- 工具 ----------

    private static string JsonHelperPretty(string raw)
        => Core.Json.JsonHelper.TryPretty(raw, out var pretty) ? pretty : raw;
}
