using System.Text.Json;
using System.Windows.Threading;
using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Json;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Mvvm;

namespace ElasticDesktopManager.ViewModels;

public class MetricCard : ObservableObject
{
    public required string Label { get; init; }
    public required string Value { get; init; }
}

public class HealthViewModel : PageViewModelBase
{
    private EsHealth? _health;
    private readonly DispatcherTimer _timer;

    public ObservableList<MetricCard> Metrics { get; } = new();

    // 以下属性必须走 SetProperty：HealthView 直接绑定它们，
    // 用自动属性会导致数据加载完成后界面仍停留在 “-”。
    private string _statusText = "-";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private string _clusterName = "-";
    public string ClusterName
    {
        get => _clusterName;
        private set => SetProperty(ref _clusterName, value);
    }

    private string _clusterUuid = "-";
    public string ClusterUuid
    {
        get => _clusterUuid;
        private set => SetProperty(ref _clusterUuid, value);
    }

    private string _nodeName = "-";
    public string NodeName
    {
        get => _nodeName;
        private set => SetProperty(ref _nodeName, value);
    }

    private string _versionNumber = "-";
    public string VersionNumber
    {
        get => _versionNumber;
        private set => SetProperty(ref _versionNumber, value);
    }

    private string _luceneVersion = "-";
    public string LuceneVersion
    {
        get => _luceneVersion;
        private set => SetProperty(ref _luceneVersion, value);
    }

    private bool _autoRefresh = true;
    public bool AutoRefresh
    {
        get => _autoRefresh;
        set
        {
            if (SetProperty(ref _autoRefresh, value))
                ToggleTimer();
        }
    }

    private bool _active = true;
    /// <summary>页面是否可见（可见时才轮询）。</summary>
    public bool Active
    {
        get => _active;
        private set
        {
            if (SetProperty(ref _active, value))
                ToggleTimer();
        }
    }

    public void SetActive(bool active) => Active = active;

    public HealthViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) =>
        {
            if (HasConnection && Active) await ReloadAsync();
        };
        ToggleTimer();
    }

    private void ToggleTimer()
    {
        if (_autoRefresh && Active) _timer.Start();
        else _timer.Stop();
    }

    public override Task ReloadAsync() => LoadHealthAsync(busy: true, silent: false);

    /// <summary>进入页面/连接建立时自动刷新：不占全局忙碌条、失败不弹模态框。</summary>
    public override Task AutoReloadAsync() => LoadHealthAsync(busy: false, silent: true);

    private async Task LoadHealthAsync(bool busy, bool silent)
    {
        if (!HasConnection) return;
        await RunAsync(async () =>
        {
            var client = Client;
            string healthJson = await client.GetClusterHealthAsync();
            _health = EsParsers.ParseHealth(healthJson);
            ApplyHealth();

            try
            {
                string infoJson = await client.GetEsInfoAsync();
                ApplyEsInfo(infoJson);
            }
            catch (Exception)
            {
                // ES 信息失败不阻塞健康展示
            }
        }, busy: false, silent: silent);
    }

    private void ApplyHealth()
    {
        if (_health is null) return;
        ClusterName = _health.ClusterName;
        StatusText = _health.Status;

        Metrics.ReplaceAll(new[]
        {
            new MetricCard { Label = Localization.L("home.status"), Value = _health.Status },
            new MetricCard { Label = Localization.L("home.nodes"), Value = _health.NumberOfNodes.ToString() },
            new MetricCard { Label = Localization.L("home.dataNodes"), Value = _health.NumberOfDataNodes.ToString() },
            new MetricCard { Label = Localization.L("home.activePrimary"), Value = _health.ActivePrimaryShards.ToString() },
            new MetricCard { Label = Localization.L("home.activeShards"), Value = _health.ActiveShards.ToString() },
            new MetricCard { Label = Localization.L("home.relocating"), Value = _health.RelocatingShards.ToString() },
            new MetricCard { Label = Localization.L("home.initializing"), Value = _health.InitializingShards.ToString() },
            new MetricCard { Label = Localization.L("home.unassigned"), Value = _health.UnassignedShards.ToString() },
            new MetricCard { Label = Localization.L("home.pendingTasks"), Value = _health.NumberOfPendingTasks.ToString() },
        });
    }

    private void ApplyEsInfo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        NodeName = JsonHelper.GetString(r, "name", "-");
        ClusterUuid = JsonHelper.GetString(r, "cluster_uuid", "-");
        if (r.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Object)
        {
            VersionNumber = JsonHelper.GetString(v, "number", "-");
            LuceneVersion = JsonHelper.GetString(v, "lucene_version", "-");
        }
    }
}