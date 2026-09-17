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

    // ================= A3 分词调试 =================

    /// <summary>POST /{index}/_analyze —— 用索引的分词器分析文本。</summary>
    public Task<string> AnalyzeTextAsync(string indexName, string text, string? field = null,
        string? analyzer = null, CancellationToken ct = default)
        => ExecuteAsync("POST", IndexPath(indexName, "_analyze"), BuildAnalyzeBody(text, field, analyzer), _timeout, ct);

    /// <summary>
    /// POST /_analyze —— 不指定索引，用内置分词器分析文本。
    /// 刻意用不同方法名：与 <see cref="AnalyzeTextAsync(string,string,string?,string?,CancellationToken)"/>
    /// 同为 (string, string?) 起始签名，重载会产生调用歧义（编译期 CS0121）。
    /// </summary>
    public Task<string> AnalyzeTextWithBuiltinAsync(string text, string? analyzer = null, CancellationToken ct = default)
        => ExecuteAsync("POST", "/_analyze", BuildAnalyzeBody(text, null, analyzer), _timeout, ct);

    private static string BuildAnalyzeBody(string text, string? field, string? analyzer)
    {
        var body = new JsonObject { ["text"] = text };
        if (!string.IsNullOrWhiteSpace(field)) body["field"] = field.Trim();
        if (!string.IsNullOrWhiteSpace(analyzer)) body["analyzer"] = analyzer.Trim();
        return body.ToJsonString();
    }

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

    // ================= A7 模板 =================

    /// <summary>GET /_index_template —— 可组合索引模板（ES 7.8+）。</summary>
    public Task<string> GetIndexTemplatesAsync(CancellationToken ct = default)
        => ExecuteAsync("GET", "/_index_template", null, _timeout, ct);

    /// <summary>GET /_component_template —— 组件模板。</summary>
    public Task<string> GetComponentTemplatesAsync(CancellationToken ct = default)
        => ExecuteAsync("GET", "/_component_template", null, _timeout, ct);

    /// <summary>PUT /_index_template/{name} —— 创建/更新可组合索引模板。</summary>
    public Task<string> PutIndexTemplateAsync(string name, string bodyJson, CancellationToken ct = default)
        => ExecuteAsync("PUT", "/_index_template/" + Uri.EscapeDataString(name), bodyJson, _timeout, ct);

    /// <summary>DELETE /_index_template/{name}</summary>
    public Task<string> DeleteIndexTemplateAsync(string name, CancellationToken ct = default)
        => ExecuteAsync("DELETE", "/_index_template/" + Uri.EscapeDataString(name), null, _timeout, ct);

    /// <summary>DELETE /_component_template/{name}</summary>
    public Task<string> DeleteComponentTemplateAsync(string name, CancellationToken ct = default)
        => ExecuteAsync("DELETE", "/_component_template/" + Uri.EscapeDataString(name), null, _timeout, ct);

    // ================= A8 诊断 =================

    /// <summary>
    /// GET /_cluster/allocation/explain —— 诊断分片未分配 / 不可移动的原因。
    /// 三者都为空时诊断第一个未分配分片（ES 默认行为）。
    /// </summary>
    public Task<string> ExplainAllocationAsync(string? index = null, int? shard = null,
        bool? primary = null, CancellationToken ct = default)
    {
        var body = new JsonObject();
        if (!string.IsNullOrWhiteSpace(index)) body["index"] = index.Trim();
        if (shard is not null) body["shard"] = shard.Value;
        if (primary is not null) body["primary"] = primary.Value;

        // 无参数时 ES 要求空 body 或显式 {}；统一发 {} 更稳定
        return ExecuteAsync("POST", "/_cluster/allocation/explain", body.ToJsonString(), _timeout, ct);
    }

    /// <summary>GET /_nodes/hot_threads —— 热点线程堆栈（纯文本响应）。</summary>
    public Task<string> HotThreadsAsync(string? nodeId = null, CancellationToken ct = default)
    {
        var path = string.IsNullOrWhiteSpace(nodeId)
            ? "/_nodes/hot_threads"
            : "/_nodes/" + Uri.EscapeDataString(nodeId.Trim()) + "/hot_threads";
        return ExecuteAsync("GET", path, null, _timeout, ct);
    }

    /// <summary>GET /_nodes/thread_pool —— 各节点线程池活跃/队列/拒绝统计。</summary>
    public Task<string> GetThreadPoolAsync(CancellationToken ct = default)
        => ExecuteAsync("GET", "/_nodes/thread_pool", null, _timeout, ct);

    /// <summary>GET /_cluster/pending_tasks —— 主节点待处理任务。</summary>
    public Task<string> GetPendingTasksAsync(CancellationToken ct = default)
        => ExecuteAsync("GET", "/_cluster/pending_tasks", null, _timeout, ct);

    // ================= A9 Force Merge =================

    /// <summary>POST /{index}/_forcemerge —— 强制段合并（maxSegments 默认 1）。</summary>
    public Task<string> ForceMergeAsync(string indexName, int maxSegments = 1, CancellationToken ct = default)
    {
        var path = IndexPath(indexName, "_forcemerge") + "?max_num_segments=" + Math.Max(1, maxSegments);
        return ExecuteAsync("POST", path, null, _sqlTimeout, ct);
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
