using System.Text.Json.Nodes;
using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Json;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.Services;
using ElasticDesktopManager.Views;

namespace ElasticDesktopManager.ViewModels;

/// <summary>仓库类型下拉项（fs 需要 path.repo；其余需要安装对应仓库插件）。</summary>
public record RepoTypeOption(string Code, string Label);

/// <summary>
/// 一行状态文字：正常时是次要色统计，失败时是危险色错误原因。
/// 用对象而不是"字符串 + bool"两个属性，XAML 里一次 DataContext 绑定就够。
/// </summary>
public class TabStatus : ObservableObject
{
    private string _text = "";
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    private bool _isError;
    public bool IsError
    {
        get => _isError;
        set => SetProperty(ref _isError, value);
    }

    public void Ok(string text)
    {
        IsError = false;
        Text = text;
    }

    public void Fail(string text)
    {
        IsError = true;
        Text = text;
    }
}

/// <summary>
/// 快照页（对齐 ES-King 的五个列表）：
///   ① 仓库管理  ② 快照管理  ③ 快照恢复  ④ 自动策略 SLM  ⑤ 生命周期 ILM
///
/// 设计要点：
/// · 每个页签独立加载、独立报错（<see cref="TabStatus"/>）——SLM/ILM 属于 x-pack 功能，
///   OpenSearch 或未启用 x-pack 的集群会直接返回错误；如果共用一个错误弹框，
///   打开页面就会连弹几个模态框，而正确的做法是把"这个集群不支持"写在对应页签里。
/// · 所有"依赖某个可写属性的只读派生属性"都在那个属性的 setter 里显式 OnPropertyChanged，
///   否则绑定不更新 → 按钮永久禁用（本仓库踩过多次）。
/// </summary>
public class SnapshotViewModel : PageViewModelBase
{
    private const int TabRepo = 0;
    private const int TabSnapshots = 1;
    private const int TabRestore = 2;
    private const int TabSlm = 3;
    private const int TabIlm = 4;

    // ================= 五个列表 =================

    public ObservableList<EsSnapshotRepository> Repositories { get; } = new();
    public ObservableList<EsSnapshot> Snapshots { get; } = new();
    public ObservableList<EsRecoveryShard> RecoveryShards { get; } = new();
    public ObservableList<EsSlmPolicy> SlmPolicies { get; } = new();
    public ObservableList<EsIlmPolicy> IlmPolicies { get; } = new();

    public TabStatus RepoStatus { get; } = new();
    public TabStatus SnapshotStatus { get; } = new();
    public TabStatus RestoreStatus { get; } = new();
    public TabStatus SlmStatus { get; } = new();
    public TabStatus IlmStatus { get; } = new();

    private int _selectedTabIndex;
    /// <summary>当前页签；切换时懒加载该页签的数据（避免进页面就打 5 个接口）。</summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (!SetProperty(ref _selectedTabIndex, value)) return;
            _ = LoadTabAsync(value);
        }
    }

    /// <summary>新建表单是否展开（默认收起，保持页面干净）。</summary>
    private bool _isFormOpen;
    public bool IsFormOpen
    {
        get => _isFormOpen;
        set => SetProperty(ref _isFormOpen, value);
    }

    public ICommand ToggleFormCommand { get; }

    // ================= ① 仓库 =================

    /// <summary>仓库类型下拉。fs 是内置的；s3/gcs/azure 需要先装仓库插件。</summary>
    public List<RepoTypeOption> RepositoryTypes { get; } = new()
    {
        new("fs", "fs"),
        new("s3", "s3"),
        new("gcs", "gcs"),
        new("azure", "azure"),
        new("hdfs", "hdfs"),
        new("url", "url"),
    };

    private string _newRepoType = "fs";
    public string NewRepoType
    {
        get => _newRepoType;
        set => SetProperty(ref _newRepoType, value);
    }

    private string _newRepoName = "";
    public string NewRepoName
    {
        get => _newRepoName;
        set => SetProperty(ref _newRepoName, value);
    }

    private string _newRepoLocation = "";
    public string NewRepoLocation
    {
        get => _newRepoLocation;
        set => SetProperty(ref _newRepoLocation, value);
    }

    private EsSnapshotRepository? _selectedRepository;
    public EsSnapshotRepository? SelectedRepository
    {
        get => _selectedRepository;
        set
        {
            if (!SetProperty(ref _selectedRepository, value)) return;
            OnPropertyChanged(nameof(HasRepository));
            OnPropertyChanged(nameof(RepositorySettingsJson));
            // 换仓库 → 快照列表与恢复页的快照下拉都必须跟着换
            Snapshots.ReplaceAll(Array.Empty<EsSnapshot>());
            _ = LoadSnapshotsAsync(silent: true);
            _ = LoadRestoreSnapshotsAsync();
        }
    }

    public bool HasRepository => SelectedRepository is not null;

    public string RepositorySettingsJson => SelectedRepository?.SettingsJson ?? "";

    // ================= ② 快照 =================

    private EsSnapshot? _selectedSnapshot;
    public EsSnapshot? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set
        {
            if (!SetProperty(ref _selectedSnapshot, value)) return;
            OnPropertyChanged(nameof(HasSnapshot));
            OnPropertyChanged(nameof(SnapshotDetail));
        }
    }

    public bool HasSnapshot => SelectedSnapshot is not null;

    /// <summary>选中快照的详情（索引列表截断 + 失败原因）。</summary>
    public string SnapshotDetail
    {
        get
        {
            if (SelectedSnapshot is not { } s) return "";
            var parts = new List<string>();
            if (s.IndexCount > 0)
            {
                string list = s.Indices.Length > 160 ? s.Indices[..160] + " …" : s.Indices;
                parts.Add($"{s.IndexCount} · {list}");
            }
            if (!string.IsNullOrEmpty(s.ShardsText)) parts.Add(s.ShardsText);
            if (!string.IsNullOrEmpty(s.Failures)) parts.Add("⚠ " + s.Failures);
            return string.Join("    ", parts);
        }
    }

    private string _newSnapshotName = "";
    public string NewSnapshotName
    {
        get => _newSnapshotName;
        set => SetProperty(ref _newSnapshotName, value);
    }

    private string _newSnapshotIndices = "";
    public string NewSnapshotIndices
    {
        get => _newSnapshotIndices;
        set => SetProperty(ref _newSnapshotIndices, value);
    }

    private bool _includeGlobalState;
    public bool IncludeGlobalState
    {
        get => _includeGlobalState;
        set => SetProperty(ref _includeGlobalState, value);
    }

    // ================= ③ 恢复 =================

    private EsSnapshotRepository? _restoreRepository;
    public EsSnapshotRepository? RestoreRepository
    {
        get => _restoreRepository;
        set
        {
            if (!SetProperty(ref _restoreRepository, value)) return;
            RestoreSnapshotName = "";
            _ = LoadRestoreSnapshotsAsync();
        }
    }

    /// <summary>所选仓库下的快照名（恢复时的下拉源）。</summary>
    public ObservableList<string> RestoreSnapshots { get; } = new();

    private string _restoreSnapshotName = "";
    public string RestoreSnapshotName
    {
        get => _restoreSnapshotName;
        set => SetProperty(ref _restoreSnapshotName, value);
    }

    private string _restoreIndices = "";
    public string RestoreIndices
    {
        get => _restoreIndices;
        set => SetProperty(ref _restoreIndices, value);
    }

    private string _renamePattern = "";
    /// <summary>重命名正则，如 index_(.+)。留空表示保持原名（同名索引已存在时会失败）。</summary>
    public string RenamePattern
    {
        get => _renamePattern;
        set => SetProperty(ref _renamePattern, value);
    }

    private string _renameReplacement = "";
    /// <summary>替换串，如 restored_$1。</summary>
    public string RenameReplacement
    {
        get => _renameReplacement;
        set => SetProperty(ref _renameReplacement, value);
    }

    private bool _restoreIncludeGlobalState;
    public bool RestoreIncludeGlobalState
    {
        get => _restoreIncludeGlobalState;
        set => SetProperty(ref _restoreIncludeGlobalState, value);
    }

    // ================= ④ SLM 自动策略 =================

    private EsSlmPolicy? _selectedSlmPolicy;
    public EsSlmPolicy? SelectedSlmPolicy
    {
        get => _selectedSlmPolicy;
        set
        {
            if (!SetProperty(ref _selectedSlmPolicy, value)) return;
            OnPropertyChanged(nameof(HasSlmPolicy));
            OnPropertyChanged(nameof(SlmPolicyJson));
        }
    }

    public bool HasSlmPolicy => SelectedSlmPolicy is not null;

    public string SlmPolicyJson => SelectedSlmPolicy?.PolicyJson ?? "";

    private string _newSlmId = "";
    public string NewSlmId
    {
        get => _newSlmId;
        set => SetProperty(ref _newSlmId, value);
    }

    private string _newSlmNameTemplate = "";
    public string NewSlmNameTemplate
    {
        get => _newSlmNameTemplate;
        set => SetProperty(ref _newSlmNameTemplate, value);
    }

    private string _newSlmSchedule = "";
    public string NewSlmSchedule
    {
        get => _newSlmSchedule;
        set => SetProperty(ref _newSlmSchedule, value);
    }

    private EsSnapshotRepository? _newSlmRepository;
    public EsSnapshotRepository? NewSlmRepository
    {
        get => _newSlmRepository;
        set => SetProperty(ref _newSlmRepository, value);
    }

    private string _newSlmIndices = "";
    public string NewSlmIndices
    {
        get => _newSlmIndices;
        set => SetProperty(ref _newSlmIndices, value);
    }

    private string _newSlmExpireAfter = "";
    public string NewSlmExpireAfter
    {
        get => _newSlmExpireAfter;
        set => SetProperty(ref _newSlmExpireAfter, value);
    }

    private int _newSlmMinCount;
    public int NewSlmMinCount
    {
        get => _newSlmMinCount;
        set => SetProperty(ref _newSlmMinCount, value);
    }

    private int _newSlmMaxCount;
    public int NewSlmMaxCount
    {
        get => _newSlmMaxCount;
        set => SetProperty(ref _newSlmMaxCount, value);
    }

    // ================= ⑤ ILM 生命周期 =================

    private EsIlmPolicy? _selectedIlmPolicy;
    public EsIlmPolicy? SelectedIlmPolicy
    {
        get => _selectedIlmPolicy;
        set
        {
            if (!SetProperty(ref _selectedIlmPolicy, value)) return;
            OnPropertyChanged(nameof(HasIlmPolicy));
            OnPropertyChanged(nameof(IlmPolicyJson));
        }
    }

    public bool HasIlmPolicy => SelectedIlmPolicy is not null;

    public string IlmPolicyJson => SelectedIlmPolicy?.PolicyJson ?? "";

    private string _newIlmId = "";
    public string NewIlmId
    {
        get => _newIlmId;
        set => SetProperty(ref _newIlmId, value);
    }

    private string _newIlmBody = "";
    public string NewIlmBody
    {
        get => _newIlmBody;
        set => SetProperty(ref _newIlmBody, value);
    }

    // ================= 命令 =================

    public ICommand RefreshCommand { get; }
    public ICommand RefreshSlmCommand { get; }
    public ICommand RefreshIlmCommand { get; }
    public ICommand CreateRepositoryCommand { get; }
    public ICommand DeleteRepositoryCommand { get; }
    public ICommand VerifyRepositoryCommand { get; }
    public ICommand CreateSnapshotCommand { get; }
    public ICommand DeleteSnapshotCommand { get; }
    public ICommand ShowSnapshotJsonCommand { get; }
    public ICommand RestoreSnapshotCommand { get; }
    public ICommand RefreshRecoveryCommand { get; }
    public ICommand CreateSlmCommand { get; }
    public ICommand DeleteSlmCommand { get; }
    public ICommand ExecuteSlmCommand { get; }
    public ICommand ShowSlmJsonCommand { get; }
    public ICommand CreateIlmCommand { get; }
    public ICommand DeleteIlmCommand { get; }
    public ICommand ShowIlmJsonCommand { get; }
    public ICommand FillIlmTemplateCommand { get; }

    public SnapshotViewModel()
    {
        ToggleFormCommand = new RelayCommand(_ => IsFormOpen = !IsFormOpen);
        RefreshCommand = new AsyncRelayCommand(_ => ReloadAsync());
        RefreshSlmCommand = new AsyncRelayCommand(_ => LoadSlmAsync());
        RefreshIlmCommand = new AsyncRelayCommand(_ => LoadIlmAsync());

        CreateRepositoryCommand = new AsyncRelayCommand(_ => CreateRepositoryAsync());
        DeleteRepositoryCommand = new AsyncRelayCommand(_ => DeleteRepositoryAsync());
        VerifyRepositoryCommand = new AsyncRelayCommand(_ => VerifyRepositoryAsync());

        CreateSnapshotCommand = new AsyncRelayCommand(_ => CreateSnapshotAsync());
        DeleteSnapshotCommand = new AsyncRelayCommand(_ => DeleteSnapshotAsync());
        ShowSnapshotJsonCommand = new AsyncRelayCommand(_ => ShowSnapshotJsonAsync());

        RestoreSnapshotCommand = new AsyncRelayCommand(_ => RestoreSnapshotAsync());
        RefreshRecoveryCommand = new AsyncRelayCommand(_ => LoadRecoveryAsync());

        CreateSlmCommand = new AsyncRelayCommand(_ => CreateSlmAsync());
        DeleteSlmCommand = new AsyncRelayCommand(_ => DeleteSlmAsync());
        ExecuteSlmCommand = new AsyncRelayCommand(_ => ExecuteSlmAsync());
        ShowSlmJsonCommand = new RelayCommand(_ => ShowPolicyJson(
            Localization.L("snapshot.tab.slm"), SelectedSlmPolicy?.PolicyId ?? "", SlmPolicyJson));

        CreateIlmCommand = new AsyncRelayCommand(_ => CreateIlmAsync());
        DeleteIlmCommand = new AsyncRelayCommand(_ => DeleteIlmAsync());
        ShowIlmJsonCommand = new RelayCommand(_ => ShowPolicyJson(
            Localization.L("snapshot.tab.ilm"), SelectedIlmPolicy?.PolicyId ?? "", IlmPolicyJson));
        FillIlmTemplateCommand = new RelayCommand(_ => NewIlmBody = IlmTemplate);

        Localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(SnapshotDetail));
        UpdateRepoStatus();
        UpdateSnapshotStatus();
    }

    private static string Message(Exception ex) => ex is EsException e ? e.Message : ex.Message;

    // ================= 加载 =================

    public override Task ReloadAsync() => RunAsync(async () =>
    {
        await LoadRepositoriesAsync();
        await LoadSnapshotsAsync(silent: true);
        await LoadTabAsync(SelectedTabIndex);
    });

    /// <summary>进入页面 / 建立连接时自动加载：不占全局忙碌条、失败不弹模态框（原因写在页签里）。</summary>
    public override Task AutoReloadAsync() => RunAsync(async () =>
    {
        await LoadRepositoriesAsync();
        await LoadSnapshotsAsync(silent: true);
        await LoadTabAsync(SelectedTabIndex);
    }, busy: false, silent: true);

    private Task LoadTabAsync(int tabIndex)
    {
        if (!HasConnection) return Task.CompletedTask;
        return tabIndex switch
        {
            TabRestore => LoadRecoveryAsync(),
            TabSlm => LoadSlmAsync(),
            TabIlm => LoadIlmAsync(),
            _ => Task.CompletedTask,
        };
    }

    private async Task LoadRepositoriesAsync()
    {
        if (!HasConnection)
        {
            RepoStatus.Ok("");
            return;
        }

        try
        {
            string json = await Client.GetSnapshotRepositoriesAsync();
            var repos = EsParsers.ParseSnapshotRepositories(json);

            string? previous = SelectedRepository?.Name;
            Repositories.ReplaceAll(repos);

            // 保持原选中项（按名称），否则默认选第一个
            var keep = previous is null ? null : repos.FirstOrDefault(r => r.Name == previous);
            SelectedRepository = keep ?? repos.FirstOrDefault();
            // 恢复页的仓库下拉默认跟随
            if (RestoreRepository is null) RestoreRepository = SelectedRepository;
            if (NewSlmRepository is null) NewSlmRepository = SelectedRepository;
            UpdateRepoStatus();
        }
        catch (Exception ex)
        {
            RepoStatus.Fail(Message(ex));
        }
    }

    private void UpdateRepoStatus()
    {
        if (RepoStatus.IsError) return;
        RepoStatus.Ok(Repositories.Count > 0
            ? Localization.L("snapshot.status.repos", Repositories.Count)
            : Localization.L("snapshot.repo.empty"));
    }

    private async Task LoadSnapshotsAsync(bool silent)
    {
        if (!HasConnection) return;
        if (SelectedRepository is not { } repo)
        {
            Snapshots.ReplaceAll(Array.Empty<EsSnapshot>());
            UpdateSnapshotStatus();
            return;
        }

        try
        {
            string json = await Client.GetSnapshotsAsync(repo.Name);
            var list = EsParsers.ParseSnapshots(json);

            string? previous = SelectedSnapshot?.Name;
            Snapshots.ReplaceAll(list);
            SelectedSnapshot = previous is null
                ? list.FirstOrDefault()
                : list.FirstOrDefault(s => s.Name == previous) ?? list.FirstOrDefault();
            UpdateSnapshotStatus();
        }
        catch (Exception ex)
        {
            SnapshotStatus.Fail(Message(ex));
            if (!silent) Ui.Error(null, SnapshotStatus.Text);
        }
    }

    private void UpdateSnapshotStatus()
    {
        if (SnapshotStatus.IsError) return;
        if (SelectedRepository is null)
        {
            SnapshotStatus.Ok(Localization.L("snapshot.needRepo"));
            return;
        }
        SnapshotStatus.Ok(Snapshots.Count > 0
            ? Localization.L("snapshot.status.snapshots", Snapshots.Count)
            : Localization.L("snapshot.noSnapshots"));
    }

    private async Task LoadRestoreSnapshotsAsync()
    {
        if (!HasConnection || RestoreRepository is not { } repo)
        {
            RestoreSnapshots.ReplaceAll(Array.Empty<string>());
            return;
        }

        try
        {
            string json = await Client.GetSnapshotsAsync(repo.Name);
            var names = EsParsers.ParseSnapshots(json).Select(s => s.Name).ToList();
            RestoreSnapshots.ReplaceAll(names);
            if (string.IsNullOrEmpty(RestoreSnapshotName) && names.Count > 0)
                RestoreSnapshotName = names[0];
        }
        catch (Exception ex)
        {
            RestoreStatus.Fail(Message(ex));
        }
    }

    private async Task LoadRecoveryAsync()
    {
        if (!HasConnection) return;

        try
        {
            string json = await Client.GetRecoveryStatusAsync();
            var rows = EsParsers.ParseRecovery(json);
            RecoveryShards.ReplaceAll(rows);
            RestoreStatus.Ok(rows.Count > 0
                ? Localization.L("snapshot.status.recovery", rows.Count)
                : Localization.L("snapshot.restore.none"));
        }
        catch (Exception ex)
        {
            RestoreStatus.Fail(Message(ex));
        }
    }

    private async Task LoadSlmAsync()
    {
        if (!HasConnection) return;

        try
        {
            string json = await Client.GetSlmPoliciesAsync();
            var list = EsParsers.ParseSlmPolicies(json);

            string? previous = SelectedSlmPolicy?.PolicyId;
            SlmPolicies.ReplaceAll(list);
            SelectedSlmPolicy = previous is null
                ? list.FirstOrDefault()
                : list.FirstOrDefault(p => p.PolicyId == previous) ?? list.FirstOrDefault();
            if (NewSlmRepository is null) NewSlmRepository = SelectedRepository;

            SlmStatus.Ok(list.Count > 0
                ? Localization.L("snapshot.status.slm", list.Count)
                : Localization.L("snapshot.slm.empty"));
        }
        catch (Exception ex)
        {
            SlmStatus.Fail(Message(ex));
        }
    }

    private async Task LoadIlmAsync()
    {
        if (!HasConnection) return;

        try
        {
            string json = await Client.GetIlmPoliciesAsync();
            var list = EsParsers.ParseIlmPolicies(json);

            string? previous = SelectedIlmPolicy?.PolicyId;
            IlmPolicies.ReplaceAll(list);
            SelectedIlmPolicy = previous is null
                ? list.FirstOrDefault()
                : list.FirstOrDefault(p => p.PolicyId == previous) ?? list.FirstOrDefault();

            IlmStatus.Ok(list.Count > 0
                ? Localization.L("snapshot.status.ilm", list.Count)
                : Localization.L("snapshot.ilm.empty"));
        }
        catch (Exception ex)
        {
            IlmStatus.Fail(Message(ex));
        }
    }

    // ================= ① 仓库操作 =================

    private async Task CreateRepositoryAsync()
    {
        if (!RequireConnection()) return;
        string name = NewRepoName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            Ui.Info(null, Localization.L("snapshot.repo.needName"));
            return;
        }

        // location 留空时用仓库名做子目录（fs 最省心的默认值）
        string location = string.IsNullOrWhiteSpace(NewRepoLocation) ? name : NewRepoLocation.Trim();
        string body = new JsonObject
        {
            ["type"] = string.IsNullOrEmpty(NewRepoType) ? "fs" : NewRepoType,
            ["settings"] = new JsonObject
            {
                ["location"] = location,
                ["compress"] = true,
            },
        }.ToJsonString();

        await RunAsync(async () =>
        {
            await Client.CreateSnapshotRepositoryAsync(name, body);
            Ui.Toast(Localization.L("snapshot.repo.created", name));
            NewRepoName = "";
            NewRepoLocation = "";
            await LoadRepositoriesAsync();
        });
    }

    private async Task DeleteRepositoryAsync()
    {
        if (!RequireConnection()) return;
        if (SelectedRepository is not { } repo)
        {
            Ui.Info(null, Localization.L("snapshot.needRepo"));
            return;
        }
        if (!Ui.Confirm(null, Localization.L("snapshot.repo.delete.prompt", repo.Name))) return;

        await RunAsync(async () =>
        {
            await Client.DeleteSnapshotRepositoryAsync(repo.Name);
            Ui.Toast(Localization.L("snapshot.repo.deleted", repo.Name));
            SelectedRepository = null;
            await LoadRepositoriesAsync();
        });
    }

    private async Task VerifyRepositoryAsync()
    {
        if (!RequireConnection()) return;
        if (SelectedRepository is not { } repo)
        {
            Ui.Info(null, Localization.L("snapshot.needRepo"));
            return;
        }

        await RunAsync(async () =>
        {
            await Client.VerifySnapshotRepositoryAsync(repo.Name);
            Ui.Toast(Localization.L("snapshot.repo.verified", repo.Name));
        });
    }

    // ================= ② 快照操作 =================

    private async Task CreateSnapshotAsync()
    {
        if (!RequireConnection()) return;
        if (SelectedRepository is not { } repo)
        {
            Ui.Info(null, Localization.L("snapshot.needRepo"));
            return;
        }

        string name = NewSnapshotName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            Ui.Info(null, Localization.L("snapshot.create.needName"));
            return;
        }

        await RunAsync(async () =>
        {
            await Client.CreateSnapshotAsync(repo.Name, name, NewSnapshotIndices, IncludeGlobalState);
            Ui.Toast(Localization.L("snapshot.created", name));
            NewSnapshotName = "";
            NewSnapshotIndices = "";
            await LoadSnapshotsAsync(silent: true);
        });
    }

    private async Task DeleteSnapshotAsync()
    {
        if (!RequireConnection()) return;
        if (SelectedRepository is not { } repo)
        {
            Ui.Info(null, Localization.L("snapshot.needRepo"));
            return;
        }
        if (SelectedSnapshot is not { } snap)
        {
            Ui.Info(null, Localization.L("snapshot.needSelection"));
            return;
        }
        if (!Ui.Confirm(null, Localization.L("snapshot.delete.prompt", snap.Name))) return;

        await RunAsync(async () =>
        {
            await Client.DeleteSnapshotAsync(repo.Name, snap.Name);
            Ui.Toast(Localization.L("snapshot.deleted", snap.Name));
            await LoadSnapshotsAsync(silent: true);
        });
    }

    private async Task ShowSnapshotJsonAsync()
    {
        if (!RequireConnection()) return;
        if (SelectedRepository is not { } repo || SelectedSnapshot is not { } snap)
        {
            Ui.Info(null, Localization.L("snapshot.needSelection"));
            return;
        }

        await RunAsync(async () =>
        {
            string json = await Client.GetSnapshotDetailAsync(repo.Name, snap.Name);
            new JsonViewerWindow(Localization.L("snapshot.detail.title"),
                JsonHelper.Pretty(json)) { Owner = Ui.Main }.ShowDialog();
        });
    }

    // ================= ③ 恢复操作 =================

    private async Task RestoreSnapshotAsync()
    {
        if (!RequireConnection()) return;
        if (RestoreRepository is not { } repo)
        {
            Ui.Info(null, Localization.L("snapshot.needRepo"));
            return;
        }
        if (string.IsNullOrWhiteSpace(RestoreSnapshotName))
        {
            Ui.Info(null, Localization.L("snapshot.restore.needSnapshot"));
            return;
        }
        if (!Ui.Confirm(null, Localization.L("snapshot.restore.prompt", RestoreSnapshotName))) return;

        await RunAsync(async () =>
        {
            await Client.RestoreSnapshotAsync(repo.Name, RestoreSnapshotName, RestoreIndices,
                RestoreIncludeGlobalState, RenamePattern, RenameReplacement);
            Ui.Toast(Localization.L("snapshot.restored", RestoreSnapshotName));
            // 恢复是异步的：立刻刷一次进度，让用户看到分片动起来
            await LoadRecoveryAsync();
        });
    }

    // ================= ④ SLM 操作 =================

    private async Task CreateSlmAsync()
    {
        if (!RequireConnection()) return;

        string id = NewSlmId.Trim();
        if (string.IsNullOrEmpty(id))
        {
            Ui.Info(null, Localization.L("snapshot.slm.needId"));
            return;
        }
        if (string.IsNullOrWhiteSpace(NewSlmSchedule))
        {
            Ui.Info(null, Localization.L("snapshot.slm.needSchedule"));
            return;
        }
        if (NewSlmRepository is not { } repo)
        {
            Ui.Info(null, Localization.L("snapshot.needRepo"));
            return;
        }

        string body = BuildSlmBody(repo.Name);
        await RunAsync(async () =>
        {
            await Client.CreateSlmPolicyAsync(id, body);
            Ui.Toast(Localization.L("snapshot.slm.created", id));
            NewSlmId = "";
            await LoadSlmAsync();
        });
    }

    /// <summary>组装 SLM 策略体；retention 只在填了值时才带上（空对象会被 ES 判为非法）。</summary>
    private string BuildSlmBody(string repository)
    {
        var indices = new JsonArray();
        foreach (var name in NewSlmIndices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            indices.Add(name);
        if (indices.Count == 0) indices.Add("*");

        var body = new JsonObject
        {
            ["schedule"] = NewSlmSchedule.Trim(),
            ["repository"] = repository,
            ["config"] = new JsonObject
            {
                ["indices"] = indices,
                ["include_global_state"] = false,
            },
        };

        if (!string.IsNullOrWhiteSpace(NewSlmNameTemplate))
            body["name"] = NewSlmNameTemplate.Trim();

        var retention = new JsonObject();
        if (!string.IsNullOrWhiteSpace(NewSlmExpireAfter)) retention["expire_after"] = NewSlmExpireAfter.Trim();
        if (NewSlmMinCount > 0) retention["min_count"] = NewSlmMinCount;
        if (NewSlmMaxCount > 0) retention["max_count"] = NewSlmMaxCount;
        if (retention.Count > 0) body["retention"] = retention;

        return body.ToJsonString();
    }

    private async Task DeleteSlmAsync()
    {
        if (!RequireConnection()) return;
        if (SelectedSlmPolicy is not { } policy)
        {
            Ui.Info(null, Localization.L("snapshot.slm.needSelection"));
            return;
        }
        if (!Ui.Confirm(null, Localization.L("snapshot.slm.delete.prompt", policy.PolicyId))) return;

        await RunAsync(async () =>
        {
            await Client.DeleteSlmPolicyAsync(policy.PolicyId);
            Ui.Toast(Localization.L("snapshot.slm.deleted", policy.PolicyId));
            await LoadSlmAsync();
        });
    }

    private async Task ExecuteSlmAsync()
    {
        if (!RequireConnection()) return;
        if (SelectedSlmPolicy is not { } policy)
        {
            Ui.Info(null, Localization.L("snapshot.slm.needSelection"));
            return;
        }
        if (!Ui.Confirm(null, Localization.L("snapshot.slm.execute.prompt", policy.PolicyId))) return;

        await RunAsync(async () =>
        {
            await Client.ExecuteSlmPolicyAsync(policy.PolicyId);
            Ui.Toast(Localization.L("snapshot.slm.executed", policy.PolicyId));
            await LoadSlmAsync();
        });
    }

    // ================= ⑤ ILM 操作 =================

    /// <summary>示例策略：hot 阶段滚动 + 30 天后删除，覆盖最常见的日志场景。</summary>
    private const string IlmTemplate = """
        {
          "policy": {
            "phases": {
              "hot": {
                "actions": {
                  "rollover": { "max_size": "50gb", "max_age": "1d" },
                  "set_priority": { "priority": 100 }
                }
              },
              "delete": {
                "min_age": "30d",
                "actions": { "delete": {} }
              }
            }
          }
        }
        """;

    private async Task CreateIlmAsync()
    {
        if (!RequireConnection()) return;

        string id = NewIlmId.Trim();
        if (string.IsNullOrEmpty(id))
        {
            Ui.Info(null, Localization.L("snapshot.ilm.needId"));
            return;
        }

        string body = NewIlmBody.Trim();
        if (string.IsNullOrEmpty(body))
        {
            NewIlmBody = IlmTemplate;
            Ui.Info(null, Localization.L("snapshot.ilm.needBody"));
            return;
        }

        // 先本地校验 JSON：ES 的报错信息很长，本地就能拦下来的错误没必要走一次网络
        try
        {
            JsonNode.Parse(body);
        }
        catch (System.Text.Json.JsonException ex)
        {
            Ui.Error(null, Localization.L("snapshot.ilm.badJson", ex.Message));
            return;
        }

        await RunAsync(async () =>
        {
            await Client.CreateIlmPolicyAsync(id, body);
            Ui.Toast(Localization.L("snapshot.ilm.created", id));
            NewIlmId = "";
            await LoadIlmAsync();
        });
    }

    private async Task DeleteIlmAsync()
    {
        if (!RequireConnection()) return;
        if (SelectedIlmPolicy is not { } policy)
        {
            Ui.Info(null, Localization.L("snapshot.ilm.needSelection"));
            return;
        }
        if (!Ui.Confirm(null, Localization.L("snapshot.ilm.delete.prompt", policy.PolicyId))) return;

        await RunAsync(async () =>
        {
            await Client.DeleteIlmPolicyAsync(policy.PolicyId);
            Ui.Toast(Localization.L("snapshot.ilm.deleted", policy.PolicyId));
            await LoadIlmAsync();
        });
    }

    private static void ShowPolicyJson(string title, string policyId, string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            Ui.Info(null, Localization.L("snapshot.needSelection"));
            return;
        }
        new JsonViewerWindow($"{title} · {policyId}", json) { Owner = Ui.Main }.ShowDialog();
    }
}
