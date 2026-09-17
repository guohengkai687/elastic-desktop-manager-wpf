using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.Services;

namespace ElasticDesktopManager.ViewModels;

/// <summary>
/// 快照管理页：仓库（列出 / 新建 / 校验 / 删除）+ 快照（列出 / 创建 / 删除 / 恢复）。
///
/// 注意（踩过的坑）：所有"依赖某个可写属性的只读派生属性"都必须在那个属性的 setter 里
/// 显式 OnPropertyChanged，否则绑定不会更新 → 按钮永久禁用。
/// 本 VM 中即 HasRepository / HasSnapshot / RepositorySettingsJson。
/// </summary>
public class SnapshotViewModel : PageViewModelBase
{
    public ObservableList<EsSnapshotRepository> Repositories { get; } = new();
    public ObservableList<EsSnapshot> Snapshots { get; } = new();

    // ---------- 仓库 ----------

    private EsSnapshotRepository? _selectedRepository;
    public EsSnapshotRepository? SelectedRepository
    {
        get => _selectedRepository;
        set
        {
            if (!SetProperty(ref _selectedRepository, value)) return;
            OnPropertyChanged(nameof(HasRepository));
            OnPropertyChanged(nameof(RepositorySettingsJson));
            Snapshots.ReplaceAll(Array.Empty<EsSnapshot>());
            _ = LoadSnapshotsAsync(silent: true);
        }
    }

    public bool HasRepository => SelectedRepository is not null;

    public string RepositorySettingsJson => SelectedRepository?.SettingsJson ?? "";

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

    // ---------- 快照 ----------

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

    private string _restoreIndices = "";
    public string RestoreIndices
    {
        get => _restoreIndices;
        set => SetProperty(ref _restoreIndices, value);
    }

    // ---------- 头部摘要 ----------

    private string _summaryText = "";
    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    // ---------- 命令 ----------

    public ICommand CreateRepositoryCommand { get; }
    public ICommand DeleteRepositoryCommand { get; }
    public ICommand VerifyRepositoryCommand { get; }
    public ICommand CreateSnapshotCommand { get; }
    public ICommand DeleteSnapshotCommand { get; }
    public ICommand RestoreSnapshotCommand { get; }

    public SnapshotViewModel()
    {
        CreateRepositoryCommand = new AsyncRelayCommand(_ => CreateRepositoryAsync());
        DeleteRepositoryCommand = new AsyncRelayCommand(_ => DeleteRepositoryAsync());
        VerifyRepositoryCommand = new AsyncRelayCommand(_ => VerifyRepositoryAsync());
        CreateSnapshotCommand = new AsyncRelayCommand(_ => CreateSnapshotAsync());
        DeleteSnapshotCommand = new AsyncRelayCommand(_ => DeleteSnapshotAsync());
        RestoreSnapshotCommand = new AsyncRelayCommand(_ => RestoreSnapshotAsync());
        Localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(SnapshotDetail));
        UpdateSummary();
    }

    public override async Task ReloadAsync()
        => await RunAsync(async () =>
        {
            await LoadRepositoriesAsync();
            await LoadSnapshotsAsync(silent: true);
        });

    public override Task AutoReloadAsync()
        => RunAsync(async () =>
        {
            await LoadRepositoriesAsync();
            await LoadSnapshotsAsync(silent: true);
        }, busy: false, silent: true);

    private async Task LoadRepositoriesAsync()
    {
        string json = await Client.GetSnapshotRepositoriesAsync();
        var repos = EsParsers.ParseSnapshotRepositories(json);

        string? previous = SelectedRepository?.Name;
        Repositories.ReplaceAll(repos);

        // 保持原选中项（按名称），否则默认选第一个
        var keep = previous is null ? null : repos.FirstOrDefault(r => r.Name == previous);
        SelectedRepository = keep ?? repos.FirstOrDefault();
        UpdateSummary();
    }

    private async Task LoadSnapshotsAsync(bool silent)
    {
        if (SelectedRepository is not { } repo)
        {
            Snapshots.ReplaceAll(Array.Empty<EsSnapshot>());
            return;
        }

        await RunAsync(async () =>
        {
            string json = await Client.GetSnapshotsAsync(repo.Name);
            var list = EsParsers.ParseSnapshots(json);

            string? previous = SelectedSnapshot?.Name;
            Snapshots.ReplaceAll(list);
            SelectedSnapshot = previous is null
                ? list.FirstOrDefault()
                : list.FirstOrDefault(s => s.Name == previous) ?? list.FirstOrDefault();
            UpdateSummary();
        }, busy: false, silent: silent);
    }

    private void UpdateSummary()
    {
        int repos = Repositories.Count;
        int snaps = Snapshots.Count;
        SummaryText = $"{repos} repos · {snaps} snapshots";
    }

    // ---------- 操作 ----------

    private async Task CreateRepositoryAsync()
    {
        if (!RequireConnection()) return;
        string name = NewRepoName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            Ui.Info(null, Localization.L("snapshot.repo.needName"));
            return;
        }

        // location 留空时用仓库名做子目录（最省心的默认值）
        string location = string.IsNullOrWhiteSpace(NewRepoLocation) ? name : NewRepoLocation.Trim();
        string body = new System.Text.Json.Nodes.JsonObject
        {
            ["type"] = "fs",
            ["settings"] = new System.Text.Json.Nodes.JsonObject
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
        if (SelectedRepository is not { } repo) return;
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
        if (SelectedRepository is not { } repo) return;

        await RunAsync(async () =>
        {
            await Client.VerifySnapshotRepositoryAsync(repo.Name);
            Ui.Toast(Localization.L("snapshot.repo.verified", repo.Name));
        });
    }

    private async Task CreateSnapshotAsync()
    {
        if (!RequireConnection()) return;
        if (SelectedRepository is not { } repo) return;

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
        if (SelectedRepository is not { } repo) return;
        if (SelectedSnapshot is not { } snap) return;
        if (!Ui.Confirm(null, Localization.L("snapshot.delete.prompt", snap.Name))) return;

        await RunAsync(async () =>
        {
            await Client.DeleteSnapshotAsync(repo.Name, snap.Name);
            Ui.Toast(Localization.L("snapshot.deleted", snap.Name));
            await LoadSnapshotsAsync(silent: true);
        });
    }

    private async Task RestoreSnapshotAsync()
    {
        if (!RequireConnection()) return;
        if (SelectedRepository is not { } repo) return;
        if (SelectedSnapshot is not { } snap) return;
        if (!Ui.Confirm(null, Localization.L("snapshot.restore.prompt", snap.Name))) return;

        await RunAsync(async () =>
        {
            await Client.RestoreSnapshotAsync(repo.Name, snap.Name, RestoreIndices, IncludeGlobalState);
            Ui.Toast(Localization.L("snapshot.restored", snap.Name));
        });
    }
}
