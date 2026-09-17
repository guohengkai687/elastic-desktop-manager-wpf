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
    public string StatusText { get; private set; } = "-";
    public string ClusterName { get; private set; } = "-";
    public string ClusterUuid { get; private set; } = "-";
    public string NodeName { get; private set; } = "-";
    public string VersionNumber { get; private set; } = "-";
    public string LuceneVersion { get; private set; } = "-";

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

    public override async Task ReloadAsync()
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
        }, busy: false);
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