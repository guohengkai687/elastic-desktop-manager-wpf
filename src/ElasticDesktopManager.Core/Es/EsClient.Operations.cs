using System.Text.Json.Nodes;

namespace ElasticDesktopManager.Core.Es;

/// <summary>
/// EsClient 的能力扩展（对应 ES-King-wails 中我们原先缺失的运维/诊断端点）。
/// 单独分文件，便于审查与维护；所有方法遵循 EsClient 既有的
/// "ExecuteAsync(method, path, body, timeout, ct)" 约定。
///
/// 路径约定：索引名一律 <c>Uri.EscapeDataString</c> 转义；路径以 '/' 开头且不得出现双斜杠。
/// </summary>
public sealed partial class EsClient
{
    // ================= A1 集群指标 =================

    /// <summary>GET /_nodes/stats —— 节点级全量指标（内存/线程池/缓存/段/网络等）。</summary>
    public Task<string> GetNodeStatsAsync(CancellationToken ct = default)
        => ExecuteAsync("GET", "/_nodes/stats", null, _timeout, ct);

    // ================= A2 Mapping / Settings =================

    /// <summary>GET /{index}/_mapping</summary>
    public Task<string> GetMappingAsync(string indexName, CancellationToken ct = default)
        => ExecuteAsync("GET", IndexPath(indexName, "_mapping"), null, _timeout, ct);

    /// <summary>PUT /{index}/_mapping —— mappingJson 为 { "properties": {...} } 结构。</summary>
    public Task<string> PutMappingAsync(string indexName, string mappingJson, CancellationToken ct = default)
        => ExecuteAsync("PUT", IndexPath(indexName, "_mapping"), mappingJson, _timeout, ct);

    /// <summary>GET /{index}/_settings</summary>
    public Task<string> GetSettingsAsync(string indexName, CancellationToken ct = default)
        => ExecuteAsync("GET", IndexPath(indexName, "_settings"), null, _timeout, ct);

    /// <summary>PUT /{index}/_settings —— settingsJson 为 { "index": {...} } 结构。</summary>
    public Task<string> PutSettingsAsync(string indexName, string settingsJson, CancellationToken ct = default)
        => ExecuteAsync("PUT", IndexPath(indexName, "_settings"), settingsJson, _timeout, ct);

    // ================= A4 字段 Top 值 =================

    /// <summary>
    /// 字段 Top 值分布 + 基数（cardinality）统计。
    /// 用 terms 聚合取前 size 个高频值，同时用 cardinality 聚合统计去重总数。
    /// <paramref name="keyword"/> 为 true 时自动给字段加 .keyword 后缀（text 字段的常规做法）。
    /// </summary>
    public Task<string> FieldTopValuesAsync(string indexName, string field, int size = 20,
        bool keyword = true, CancellationToken ct = default)
    {
        var target = keyword && !field.EndsWith(".keyword", StringComparison.Ordinal)
            ? field + ".keyword"
            : field;

        var body = new JsonObject
        {
            ["size"] = 0,
            ["aggs"] = new JsonObject
            {
                ["top_values"] = new JsonObject
                {
                    ["terms"] = new JsonObject
                    {
                        ["field"] = target,
                        ["size"] = Math.Clamp(size, 1, 1000),
                    },
                },
                ["distinct_count"] = new JsonObject
                {
                    ["cardinality"] = new JsonObject { ["field"] = target },
                },
            },
        };
        return ExecuteAsync("POST", IndexPath(indexName, "_search"), body.ToJsonString(), _timeout, ct);
    }

    // ================= A5 别名管理 =================

    /// <summary>GET /_alias —— 全部别名。</summary>
    public Task<string> GetAliasesAsync(CancellationToken ct = default)
        => ExecuteAsync("GET", "/_alias", null, _timeout, ct);

    /// <summary>GET /{index}/_alias —— 指定索引的别名。</summary>
    public Task<string> GetIndexAliasesAsync(string indexName, CancellationToken ct = default)
        => ExecuteAsync("GET", IndexPath(indexName, "_alias"), null, _timeout, ct);

    /// <summary>
    /// POST /_aliases —— 添加别名。filterJson 可为 null（不过滤）；
    /// routing 可为 null。空 filter 不写 filter 字段（避免 ES 报错）。
    /// </summary>
    public Task<string> AddAliasAsync(string indexName, string alias, string? filterJson = null,
        string? routing = null, CancellationToken ct = default)
    {
        var add = new JsonObject
        {
            ["index"] = indexName,
            ["alias"] = alias,
        };
        if (!string.IsNullOrWhiteSpace(routing)) add["routing"] = routing.Trim();

        if (!string.IsNullOrWhiteSpace(filterJson))
        {
            // filter 必须是合法 JSON 对象；非法时统一抛 EsException（不让 JsonException 泄漏给调用方）
            JsonObject filter;
            try
            {
                filter = JsonNode.Parse(filterJson) as JsonObject
                    ?? throw new EsException("别名的 filter 必须是合法的 JSON 对象");
            }
            catch (System.Text.Json.JsonException)
            {
                throw new EsException("别名的 filter 不是合法的 JSON");
            }
            add["filter"] = filter;
        }

        var body = new JsonObject { ["actions"] = new JsonArray { new JsonObject { ["add"] = add } } };
        return ExecuteAsync("POST", "/_aliases", body.ToJsonString(), _timeout, ct);
    }

    /// <summary>POST /_aliases —— 移除别名。</summary>
    public Task<string> RemoveAliasAsync(string indexName, string alias, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["actions"] = new JsonArray
            {
                new JsonObject
                {
                    ["remove"] = new JsonObject { ["index"] = indexName, ["alias"] = alias },
                },
            },
        };
        return ExecuteAsync("POST", "/_aliases", body.ToJsonString(), _timeout, ct);
    }

    // ================= A6 Reindex =================

    /// <summary>
    /// POST /_reindex —— 异步数据迁移。queryJson 可为 null（迁移全部）；
    /// 传入时必须是含 "query" 的 DSL 或裸 query 对象。
    /// </summary>
    public Task<string> ReindexAsync(string source, string dest, string? queryJson = null,
        CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["source"] = new JsonObject { ["index"] = source },
            ["dest"] = new JsonObject { ["index"] = dest },
        };

        if (!string.IsNullOrWhiteSpace(queryJson))
        {
            JsonObject parsed;
            try
            {
                parsed = JsonNode.Parse(queryJson) as JsonObject
                    ?? throw new EsException("Reindex 查询条件必须是合法的 JSON 对象");
            }
            catch (System.Text.Json.JsonException)
            {
                throw new EsException("Reindex 查询条件不是合法的 JSON");
            }

            // 允许传完整 DSL（含 query 键）或裸 query 体
            var sourceNode = (JsonObject)body["source"]!;
            sourceNode["query"] = parsed["query"] is JsonNode q ? q.DeepClone() : parsed.DeepClone();
        }

        return ExecuteAsync("POST", "/_reindex", body.ToJsonString(), _sqlTimeout, ct);
    }

    // ================= A9 Force Merge =================

    /// <summary>POST /{index}/_forcemerge —— 强制段合并（maxSegments 默认 1）。</summary>
    public Task<string> ForceMergeAsync(string indexName, int maxSegments = 1, CancellationToken ct = default)
    {
        var path = IndexPath(indexName, "_forcemerge") + "?max_num_segments=" + Math.Max(1, maxSegments);
        return ExecuteAsync("POST", path, null, _sqlTimeout, ct);
    }

    // ================= A10 快照管理 =================

    /// <summary>GET /_snapshot —— 全部快照仓库。</summary>
    public Task<string> GetSnapshotRepositoriesAsync(CancellationToken ct = default)
        => ExecuteAsync("GET", "/_snapshot", null, _timeout, ct);

    /// <summary>PUT /_snapshot/{repository} —— 创建/更新仓库；body 必须含 type 与 settings。</summary>
    public Task<string> CreateSnapshotRepositoryAsync(string repository, string bodyJson, CancellationToken ct = default)
        => ExecuteAsync("PUT", "/_snapshot/" + Uri.EscapeDataString(repository), bodyJson, _sqlTimeout, ct);

    /// <summary>DELETE /_snapshot/{repository} —— 只删仓库定义；磁盘上的快照文件需手动清理。</summary>
    public Task<string> DeleteSnapshotRepositoryAsync(string repository, CancellationToken ct = default)
        => ExecuteAsync("DELETE", "/_snapshot/" + Uri.EscapeDataString(repository), null, _sqlTimeout, ct);

    /// <summary>POST /_snapshot/{repository}/_verify —— 校验仓库可用性（读写权限、并发等）。</summary>
    public Task<string> VerifySnapshotRepositoryAsync(string repository, CancellationToken ct = default)
        => ExecuteAsync("POST", "/_snapshot/" + Uri.EscapeDataString(repository) + "/_verify", null, _sqlTimeout, ct);

    /// <summary>GET /_snapshot/{repository}/_all —— 该仓库下的全部快照。</summary>
    public Task<string> GetSnapshotsAsync(string repository, CancellationToken ct = default)
        => ExecuteAsync("GET", "/_snapshot/" + Uri.EscapeDataString(repository) + "/_all", null, _timeout, ct);

    /// <summary>GET /_snapshot/_status —— 进行中的快照/恢复进度。</summary>
    public Task<string> GetSnapshotStatusAsync(CancellationToken ct = default)
        => ExecuteAsync("GET", "/_snapshot/_status", null, _timeout, ct);

    /// <summary>
    /// PUT /_snapshot/{repository}/{snapshot} —— 创建快照。
    /// 用 wait_for_completion=false 立即返回，避免大集群下请求长时间挂住 UI。
    /// </summary>
    public Task<string> CreateSnapshotAsync(string repository, string snapshot, string? indices = null,
        bool includeGlobalState = false, CancellationToken ct = default)
    {
        string path = $"/_snapshot/{Uri.EscapeDataString(repository)}/{Uri.EscapeDataString(snapshot)}"
                      + "?wait_for_completion=false";
        return ExecuteAsync("PUT", path, SnapshotBody(indices, includeGlobalState), _sqlTimeout, ct);
    }

    /// <summary>DELETE /_snapshot/{repository}/{snapshot}</summary>
    public Task<string> DeleteSnapshotAsync(string repository, string snapshot, CancellationToken ct = default)
        => ExecuteAsync("DELETE",
            $"/_snapshot/{Uri.EscapeDataString(repository)}/{Uri.EscapeDataString(snapshot)}", null, _sqlTimeout, ct);

    /// <summary>
    /// POST /_snapshot/{repository}/{snapshot}/_restore —— 恢复到当前集群。
    /// 同样用 wait_for_completion=false；恢复到已存在的同名索引会失败（由 ES 校验）。
    /// </summary>
    public Task<string> RestoreSnapshotAsync(string repository, string snapshot, string? indices = null,
        bool includeGlobalState = false, CancellationToken ct = default)
    {
        string path = $"/_snapshot/{Uri.EscapeDataString(repository)}/{Uri.EscapeDataString(snapshot)}/_restore"
                      + "?wait_for_completion=false";
        return ExecuteAsync("POST", path, SnapshotBody(indices, includeGlobalState), _sqlTimeout, ct);
    }

    /// <summary>快照/恢复共用的 body：索引为空视为全部（*）。</summary>
    private static string SnapshotBody(string? indices, bool includeGlobalState)
    {
        var body = new JsonObject
        {
            ["indices"] = string.IsNullOrWhiteSpace(indices) ? "*" : indices.Trim(),
            ["ignore_unavailable"] = true,
            ["include_global_state"] = includeGlobalState,
        };
        return body.ToJsonString();
    }

    // ================= 工具 =================

    /// <summary>
    /// 拼接 "/{index}/{suffix}"，索引名做 URL 转义。
    /// 索引名可为逗号分隔的多索引（如 "a,b"）或 "_all"，此时逐个转义后保留逗号。
    /// </summary>
    private static string IndexPath(string indexName, string suffix)
    {
        var escaped = string.Join(",", indexName
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.EscapeDataString));
        return $"/{escaped}/{suffix}";
    }
}
