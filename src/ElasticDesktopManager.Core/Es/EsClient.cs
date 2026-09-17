using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ElasticDesktopManager.Core.Models;

namespace ElasticDesktopManager.Core.Es;

/// <summary>
/// Elasticsearch REST 客户端（源项目 RestHighLevelClient 的等价物）。
/// 一个连接对应一个实例：持有 HttpClient（含认证与 SSL 校验策略），跨请求复用连接池。
///
/// SSL 支持：基地址可为 http 或 https（见 <see cref="ConfigProperty.BaseUrl"/>）。
/// 跳过 SSL 验证：<see cref="ConfigProperty.SkipSslVerify"/> 为 true 时，
/// 通过 <see cref="HttpClientHandler.ServerCertificateCustomValidationCallback"/> 接受任意服务器证书
/// （适用于自签名证书 / 内网 CA）。
/// </summary>
public sealed partial class EsClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly ConfigProperty _config;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _sqlTimeout;
    private bool _disposed;

    public ConfigProperty Config => _config;

    public EsClient(ConfigProperty config, int timeoutSec = 60, int sqlTimeoutSec = 120)
        : this(config, CreateHandler(config), timeoutSec, sqlTimeoutSec)
    {
    }

    /// <summary>测试用：注入自定义 HttpMessageHandler。</summary>
    internal EsClient(ConfigProperty config, HttpMessageHandler handler, int timeoutSec, int sqlTimeoutSec)
    {
        _config = config;
        _timeout = TimeSpan.FromMilliseconds(Math.Max(1000, timeoutSec * 1000));
        _sqlTimeout = TimeSpan.FromMilliseconds(Math.Max(1000, sqlTimeoutSec * 1000));

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(config.BaseUrl()),
            Timeout = Timeout.InfiniteTimeSpan, // 超时由每次请求自行控制
        };
        _http.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    /// <summary>构造 HttpClientHandler：按 <see cref="ConfigProperty.SkipSslVerify"/> 决定是否跳过证书校验。</summary>
    internal static HttpClientHandler CreateHandler(ConfigProperty config)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true,
        };

        if (config.SkipSslVerify)
        {
            // ★ 跳过 SSL 证书验证（自签名/内网证书）
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        return handler;
    }

    /// <summary>GET /_cluster/health</summary>
    public Task<string> GetClusterHealthAsync(CancellationToken ct = default)
        => ExecuteAsync("GET", "/_cluster/health", null, _timeout, ct);

    /// <summary>GET /（ES 版本信息）</summary>
    public Task<string> GetEsInfoAsync(CancellationToken ct = default)
        => ExecuteAsync("GET", "/", null, _timeout, ct);

    public Task<string> GetNodesAsync(CancellationToken ct = default)
        => ExecuteAsync("GET", "/_cat/nodes?format=json&h=id,name,ip,port,http_address,version,flavor,type,build,jdk,disk.total,disk.used,disk.avail,disk.used_percent,heap.current,heap.percent,heap.max,ram.current,ram.percent,ram.max,file_desc.current,file_desc.percent,file_desc.max,cpu,load_1m,load_5m,load_15m,uptime,node.role,master,completion.size,fielddata.memory_size,fielddata.evictions,query_cache.memory_size,query_cache.evictions,request_cache.memory_size,request_cache.evictions,request_cache.hit_count,request_cache.miss_count,flush.total,flush.total_time,get.current,get.time,get.total,get.exists_time,get.exists_total,get.missing_time,get.missing_total,indexing.delete_current,indexing.delete_time,indexing.delete_total,indexing.index_current,indexing.index_time,indexing.index_total,indexing.index_failed,merges.current,merges.current_docs,merges.current_size,merges.total,merges.total_docs,merges.total_size,merges.total_time,refresh.total,refresh.time,refresh.external_total,refresh.external_time,refresh.listeners,script.compilations,script.cache_evictions,script.compilation_limit_triggered,search.fetch_current,search.fetch_time,search.fetch_total,search.open_contexts,search.query_current,search.query_time,search.query_total,search.scroll_current,search.scroll_time,search.scroll_total,segments.count,segments.memory,segments.index_writer_memory,segments.version_map_memory,suggest.current,suggest.time,suggest.total", null, _timeout, ct);

    public Task<string> GetShardsAsync(CancellationToken ct = default)
        => ExecuteAsync("GET", "/_cat/shards?format=json", null, _timeout, ct);

    public const string IndicesFormat =
        "/_cat/indices?format=json&h=index,health,pri,rep,docs.count,status,tm,uuid,store.size,memory.total,creation.date";

    /// <summary>索引列表（format 为 _cat/indices 查询串，如 EsClient.IndicesFormat）。</summary>
    public Task<string> GetIndicesAsync(string format, CancellationToken ct = default)
        => ExecuteAsync("GET", format.StartsWith("/") ? format : "/" + format, null, _timeout, ct);

    public Task<string> GetIndexDetailsAsync(string indexName, CancellationToken ct = default)
        => ExecuteAsync("GET", "/" + Uri.EscapeDataString(indexName), null, _timeout, ct);

    public Task<string> GetIndexStatsAsync(string indexName, CancellationToken ct = default)
        => ExecuteAsync("GET", "/" + Uri.EscapeDataString(indexName) + "/_stats", null, _timeout, ct);

    public Task<string> RefreshIndexAsync(string indexName, CancellationToken ct = default)
        => ExecuteAsync("PUT", "/" + Uri.EscapeDataString(indexName) + "/_refresh", null, _timeout, ct);

    public Task<string> FlushIndexAsync(string indexName, CancellationToken ct = default)
        => ExecuteAsync("PUT", "/" + Uri.EscapeDataString(indexName) + "/_flush", null, _timeout, ct);

    public Task<string> ClearIndexCacheAsync(string indexName, CancellationToken ct = default)
        => ExecuteAsync("PUT", "/" + Uri.EscapeDataString(indexName) + "/_cache/clear", null, _timeout, ct);

    public Task<string> CloseIndexAsync(string indexName, CancellationToken ct = default)
        => ExecuteAsync("POST", "/" + Uri.EscapeDataString(indexName) + "/_close", null, _timeout, ct);

    public Task<string> OpenIndexAsync(string indexName, CancellationToken ct = default)
        => ExecuteAsync("POST", "/" + Uri.EscapeDataString(indexName) + "/_open", null, _timeout, ct);

    public Task<string> ListTemplatesAsync(CancellationToken ct = default)
        => ExecuteAsync("GET", "/_template", null, _timeout, ct);

    public Task<string> DeleteTemplateAsync(string templateName, CancellationToken ct = default)
        => ExecuteAsync("DELETE", "/_template/" + Uri.EscapeDataString(templateName), null, _timeout, ct);

    // ---- 以下为保留 API（暂未接入 UI，与源项目 ElasticManage 保持一致）----
    public Task<string> DeleteDocumentByIdAsync(string index, string type, string id, CancellationToken ct = default)
        => ExecuteAsync("DELETE", $"/{Uri.EscapeDataString(index)}/{Uri.EscapeDataString(type)}/{Uri.EscapeDataString(id)}", null, _sqlTimeout, ct);

    /// <summary>按索引搜索。</summary>
    public Task<string> SearchByIndexAsync(string indexName, string body, int? timeoutSec = null, CancellationToken ct = default)
    {
        var timeout = timeoutSec is null ? _timeout : TimeSpan.FromMilliseconds(Math.Max(1000, timeoutSec.Value * 1000));
        return ExecuteAsync("POST", "/" + Uri.EscapeDataString(indexName) + "/_search", body, timeout, ct);
    }

    /// <summary>按查询删除文档。</summary>
    public Task<string> DeleteByQueryAsync(string indexName, string body, CancellationToken ct = default)
        => ExecuteAsync("POST", "/" + Uri.EscapeDataString(indexName) + "/_delete_by_query", body, _timeout, ct);

    /// <summary>按查询更新文档。</summary>
    public Task<string> UpdateByQueryAsync(string indexName, string body, CancellationToken ct = default)
        => ExecuteAsync("POST", "/" + Uri.EscapeDataString(indexName) + "/_update_by_query", body, _timeout, ct);

    /// <summary>执行 SQL（fetch_size 控制每批行数）。</summary>
    public Task<string> ExecuteSqlAsync(string query, int fetchSize, CancellationToken ct = default)
    {
        string body = System.Text.Json.JsonSerializer.Serialize(new { query, fetch_size = fetchSize });
        return ExecuteAsync("POST", "/_sql?format=json", body, _sqlTimeout, ct);
    }

    public Task<string> ExecuteNextSqlAsync(string cursor, CancellationToken ct = default)
    {
        string body = System.Text.Json.JsonSerializer.Serialize(new { cursor });
        return ExecuteAsync("POST", "/_sql?format=json", body, _sqlTimeout, ct);
    }

    public Task<string> CloseSqlAsync(string cursor, CancellationToken ct = default)
    {
        string body = System.Text.Json.JsonSerializer.Serialize(new { cursor });
        return ExecuteAsync("POST", "/_sql/close", body, _sqlTimeout, ct);
    }

    /// <summary>执行任意 REST 请求（method 大写；path 必须以 / 开头；body 允许为空）。</summary>
    public Task<string> ExecuteRestAsync(string method, string path, string? body, CancellationToken ct = default)
        => ExecuteAsync(method.ToUpperInvariant(), path, body, _timeout, ct);

    /// <summary>
    /// 统一请求执行：拼 URL、加认证头、按超时取消、解析错误。
    /// </summary>
    public async Task<string> ExecuteAsync(string method, string path, string? body, TimeSpan timeout, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(path))
            throw new EsException("请求路径不能为空");
        if (!path.StartsWith('/'))
            path = "/" + path;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        if (_config.Security && !string.IsNullOrEmpty(_config.Username))
        {
            string cred = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config.Username}:{_config.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", cred);
        }

        if (!string.IsNullOrEmpty(body))
        {
            // 与源项目 executeRest 的 setJsonEntity 语义一致：任意方法都可携带 JSON 请求体
            // （如 GET /_search 带查询体）。body 为空时不设置 Content。
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new EsException($"请求超时（{timeout.TotalSeconds:0.#}s）：{method} {path}");
        }
        catch (HttpRequestException ex)
        {
            throw TranslateConnectionError(ex, $"{method} {path}");
        }

        try
        {
            using (response)
            {
                string text = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                    return text;

                string reason = string.IsNullOrWhiteSpace(text)
                    ? response.ReasonPhrase ?? "未知错误"
                    : TryExtractError(text, reasonPhrase: response.ReasonPhrase);

                throw new EsException(
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} — {reason}",
                    (int)response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new EsException($"请求超时（{timeout.TotalSeconds:0.#}s）：{method} {path}");
        }
    }

    private static string TryExtractError(string body, string? reasonPhrase)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var err = Json.JsonHelper.GetString(doc.RootElement, "error");
                if (!string.IsNullOrWhiteSpace(err))
                    return err.Length > 300 ? err[..300] : err;
            }
        }
        catch (Exception)
        {
            // 忽略解析失败，回退到原始文本
        }
        var trimmed = body.Trim();
        return trimmed.Length > 200 ? trimmed[..200] : trimmed;
    }

    private static EsException TranslateConnectionError(HttpRequestException ex, string what)
    {
        if (ex.InnerException is System.Security.Authentication.AuthenticationException)
            return new EsException("SSL/TLS 握手失败，请检查协议与证书，或勾选“跳过 SSL 验证”", inner: ex);
        if (ex.InnerException is System.Net.Sockets.SocketException se)
            return new EsException($"无法连接到 Elasticsearch（{se.Message}），请检查地址/端口/网络", inner: ex);
        return new EsException($"请求失败：{ex.Message}（{what}）", inner: ex);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}