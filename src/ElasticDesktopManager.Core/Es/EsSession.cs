using ElasticDesktopManager.Core.Models;

namespace ElasticDesktopManager.Core.Es;

/// <summary>
/// 会话管理器：持有“当前连接”的 EsClient（源项目 ElasticManage 静态 Map 的等价物）。
/// 切换连接时释放旧客户端。
/// </summary>
public sealed class EsSession : IDisposable
{
    public static EsSession Instance { get; } = new();

    private readonly object _lock = new();

    public EsClient? Current { get; private set; }
    public ConfigProperty? CurrentConfig { get; private set; }
    public bool IsConnected => Current is not null;

    /// <summary>建立连接（先探测 /_cluster/health）并设为当前连接。</summary>
    public EsClient Connect(ConfigProperty config, int timeoutSec, int sqlTimeoutSec)
    {
        EsClient client = new(config, timeoutSec, sqlTimeoutSec);

        // 验证连通性
        try
        {
            client.GetClusterHealthAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            client.Dispose();
            throw;
        }

        lock (_lock)
        {
            Current?.Dispose();
            Current = client;
            CurrentConfig = config;
        }
        return client;
    }

    /// <summary>仅测试连通性，不切换当前连接。</summary>
    public static bool TryTest(ConfigProperty config, int timeoutSec, int sqlTimeoutSec, out string error)
    {
        try
        {
            using var client = new EsClient(config, timeoutSec, sqlTimeoutSec);
            client.GetClusterHealthAsync().GetAwaiter().GetResult();
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex is EsException e ? e.Message : ex.Message;
            return false;
        }
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            Current?.Dispose();
            Current = null;
            CurrentConfig = null;
        }
    }

    public void Dispose()
    {
        Disconnect();
    }
}