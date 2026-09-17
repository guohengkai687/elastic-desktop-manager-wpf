using System.Text.Json;
using ElasticDesktopManager.Core.Json;
using ElasticDesktopManager.Core.Models;

namespace ElasticDesktopManager.Core.Es;

/// <summary>把 ES REST 响应解析为强类型模型。</summary>
public static class EsParsers
{
    public static EsHealth ParseHealth(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        return new EsHealth
        {
            ClusterName = JsonHelper.GetString(r, "cluster_name"),
            Status = JsonHelper.GetString(r, "status"),
            NumberOfNodes = GetInt(r, "number_of_nodes"),
            NumberOfDataNodes = GetInt(r, "number_of_data_nodes"),
            ActivePrimaryShards = GetInt(r, "active_primary_shards"),
            ActiveShards = GetInt(r, "active_shards"),
            RelocatingShards = GetInt(r, "relocating_shards"),
            InitializingShards = GetInt(r, "initializing_shards"),
            UnassignedShards = GetInt(r, "unassigned_shards"),
            DelayedUnassignedShards = GetInt(r, "delayed_unassigned_shards"),
            NumberOfPendingTasks = GetInt(r, "number_of_pending_tasks"),
            NumberOfInFlightFetch = GetInt(r, "number_of_in_flight_fetch"),
            TaskMaxWaitingInQueueMillis = GetDouble(r, "task_max_waiting_in_queue_millis"),
            TimedOut = r.TryGetProperty("timed_out", out var to) && to.ValueKind == JsonValueKind.True,
        };
    }

    public static List<EsIndex> ParseIndices(string json)
    {
        var list = new List<EsIndex>();
        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            list.Add(new EsIndex
            {
                Index = JsonHelper.GetString(el, "index"),
                Health = JsonHelper.GetString(el, "health"),
                Status = JsonHelper.GetString(el, "status"),
                Uuid = JsonHelper.GetString(el, "uuid"),
                Pri = JsonHelper.GetString(el, "pri"),
                Rep = JsonHelper.GetString(el, "rep"),
                DocsCount = JsonHelper.GetString(el, "docs.count"),
                StoreSize = JsonHelper.GetString(el, "store.size"),
                MemoryTotal = JsonHelper.GetString(el, "memory.total"),
                CreationDate = JsonHelper.GetString(el, "creation.date"),
            });
        }
        return list;
    }

    public static List<EsNode> ParseNodes(string json)
    {
        var list = new List<EsNode>();
        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            list.Add(new EsNode
            {
                Name = JsonHelper.GetString(el, "name"),
                Ip = JsonHelper.GetString(el, "ip"),
                Port = JsonHelper.GetString(el, "port"),
                Version = JsonHelper.GetString(el, "version"),
                NodeRole = JsonHelper.GetString(el, "node.role"),
                Master = JsonHelper.GetString(el, "master"),
                Cpu = JsonHelper.GetString(el, "cpu"),
                HeapPercent = JsonHelper.GetString(el, "heap.percent"),
                RamPercent = JsonHelper.GetString(el, "ram.percent"),
                DiskUsedPercent = JsonHelper.GetString(el, "disk.used_percent"),
                Load1m = JsonHelper.GetString(el, "load_1m"),
                Uptime = JsonHelper.GetString(el, "uptime"),
                Jdk = JsonHelper.GetString(el, "jdk"),
            });
        }
        return list;
    }

    public static List<EsShard> ParseShards(string json)
    {
        var list = new List<EsShard>();
        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            list.Add(new EsShard
            {
                Index = JsonHelper.GetString(el, "index"),
                Shard = JsonHelper.GetString(el, "shard"),
                PriRep = JsonHelper.GetString(el, "prirep"),
                State = JsonHelper.GetString(el, "state"),
                Docs = JsonHelper.GetString(el, "docs"),
                Store = JsonHelper.GetString(el, "store"),
                Ip = JsonHelper.GetString(el, "ip"),
                Node = JsonHelper.GetString(el, "node"),
            });
        }
        return list;
    }

    public static EsSqlResult ParseSqlResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;

        var result = new EsSqlResult
        {
            Took = GetInt(r, "took"),
            IsPartial = r.TryGetProperty("is_partial", out var p) && p.ValueKind == JsonValueKind.True,
            IsAsync = r.TryGetProperty("is_async", out var a) && a.ValueKind == JsonValueKind.True,
        };

        if (r.TryGetProperty("cursor", out var c) && c.ValueKind == JsonValueKind.String)
            result.Cursor = c.GetString();

        if (r.TryGetProperty("columns", out var cols) && cols.ValueKind == JsonValueKind.Array)
        {
            foreach (var col in cols.EnumerateArray())
                result.Columns.Add(JsonHelper.GetString(col, "name"));
        }

        if (r.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                var cells = new List<object?>();
                foreach (var cell in row.EnumerateArray())
                    cells.Add(JsonHelper.JsonValueToObject(cell));
                result.Rows.Add(cells);
            }
        }
        return result;
    }

    public static EsSearchResult ParseSearchResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;

        var result = new EsSearchResult
        {
            Took = GetInt(r, "took"),
            TimedOut = r.TryGetProperty("timed_out", out var to) && to.ValueKind == JsonValueKind.True,
        };

        if (r.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Object)
        {
            if (hits.TryGetProperty("total", out var total))
            {
                result.TotalHits = total.ValueKind switch
                {
                    JsonValueKind.Number => total.GetInt64(),
                    JsonValueKind.Object => GetLong(total, "value"),
                    _ => 0,
                };
            }

            if (hits.TryGetProperty("hits", out var hitArr) && hitArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var hit in hitArr.EnumerateArray())
                {
                    var src = new Dictionary<string, object?>();
                    if (hit.TryGetProperty("_source", out var s) && s.ValueKind == JsonValueKind.Object)
                        foreach (var prop in s.EnumerateObject())
                            src[prop.Name] = JsonHelper.JsonValueToObject(prop.Value);

                    result.Hits.Add(new EsSearchHit
                    {
                        Id = JsonHelper.GetString(hit, "_id"),
                        Index = JsonHelper.GetString(hit, "_index"),
                        Score = hit.TryGetProperty("_score", out var sc) && sc.ValueKind == JsonValueKind.Number
                            ? sc.GetDouble() : 0,
                        Source = src,
                    });
                }
            }
        }

        if (r.TryGetProperty("aggregations", out var agg) && agg.ValueKind == JsonValueKind.Object)
        {
            result.Aggregations = new Dictionary<string, object?>();
            foreach (var prop in agg.EnumerateObject())
                result.Aggregations[prop.Name] = JsonHelper.JsonValueToObject(prop.Value);
        }

        return result;
    }

    // ================= 新增：分词 / 模板 / 字段 Top 值 =================

    /// <summary>解析 _analyze 响应的 tokens 数组。</summary>
    public static List<AnalyzeToken> ParseAnalyzeTokens(string json)
    {
        var list = new List<AnalyzeToken>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("tokens", out var tokens) ||
            tokens.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var el in tokens.EnumerateArray())
        {
            list.Add(new AnalyzeToken
            {
                Token = JsonHelper.GetString(el, "token"),
                StartOffset = GetInt(el, "start_offset"),
                EndOffset = GetInt(el, "end_offset"),
                Type = JsonHelper.GetString(el, "type"),
                Position = GetInt(el, "position"),
            });
        }
        return list;
    }

    /// <summary>
    /// 解析索引模板 / 组件模板列表。两类响应结构一致：
    /// <c>{ "&lt;name&gt;": { "index_patterns": [...], "composed_of": [...], "priority": n, "template": {...}, "_meta": {...} } }</c>
    /// </summary>
    public static List<EsTemplate> ParseTemplates(string json)
    {
        var list = new List<EsTemplate>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var el = prop.Value;
            list.Add(new EsTemplate
            {
                Name = prop.Name,
                IndexPatterns = JoinStringArray(el, "index_patterns"),
                ComposedOf = JoinStringArray(el, "composed_of"),
                Priority = el.TryGetProperty("priority", out var pr) && pr.ValueKind == JsonValueKind.Number
                    ? pr.GetInt32().ToString()
                    : "",
                Version = el.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number
                    ? v.GetInt64().ToString()
                    : "",
                BodyJson = JsonHelper.Pretty(prop.Value.GetRawText()),
            });
        }
        return list.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>解析字段 Top 值聚合（terms + cardinality）。</summary>
    public static FieldTopValuesResult ParseFieldTopValues(string json)
    {
        var result = new FieldTopValuesResult();
        using var doc = JsonDocument.Parse(json);

        // ES 在字段不存在/不可聚合时返回 error 结构
        if (doc.RootElement.TryGetProperty("error", out var err))
        {
            result.Error = err.ValueKind == JsonValueKind.Object
                ? JsonHelper.GetString(err, "reason", err.GetRawText())
                : err.GetRawText();
            return result;
        }

        if (!doc.RootElement.TryGetProperty("aggregations", out var aggs) ||
            aggs.ValueKind != JsonValueKind.Object)
            return result;

        if (aggs.TryGetProperty("distinct_count", out var dc) &&
            dc.ValueKind == JsonValueKind.Object &&
            dc.TryGetProperty("value", out var dcv) && dcv.ValueKind == JsonValueKind.Number)
            result.DistinctCount = dcv.GetInt64();

        if (aggs.TryGetProperty("top_values", out var tv) &&
            tv.ValueKind == JsonValueKind.Object &&
            tv.TryGetProperty("buckets", out var buckets) &&
            buckets.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in buckets.EnumerateArray())
            {
                // key 可能是字符串或数字（数值字段聚合）
                string key = b.TryGetProperty("key_as_string", out var kas)
                    ? kas.GetString() ?? ""
                    : b.TryGetProperty("key", out var k)
                        ? k.ValueKind == JsonValueKind.String ? k.GetString() ?? "" : k.ToString()
                        : "";
                long count = b.TryGetProperty("doc_count", out var c) && c.ValueKind == JsonValueKind.Number
                    ? c.GetInt64()
                    : 0;
                result.Values.Add(new FieldTopValue { Value = key, Count = count });
            }
        }
        return result;
    }

    /// <summary>
    /// 解析别名查询响应。兼容两种形态：
    /// ① <c>GET /{index}/_alias</c>：<c>{ "&lt;index&gt;": { "aliases": { "&lt;alias&gt;": {...} } } }</c>
    /// ② <c>GET /_alias</c>：结构与①相同，只是索引可能多个。
    /// </summary>
    public static List<EsAlias> ParseAliases(string json)
    {
        var list = new List<EsAlias>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;

        foreach (var indexProp in doc.RootElement.EnumerateObject())
        {
            if (indexProp.Value.ValueKind != JsonValueKind.Object) continue;
            if (!indexProp.Value.TryGetProperty("aliases", out var aliases) ||
                aliases.ValueKind != JsonValueKind.Object) continue;

            foreach (var aliasProp in aliases.EnumerateObject())
            {
                var a = aliasProp.Value;
                list.Add(new EsAlias
                {
                    Name = aliasProp.Name,
                    Index = indexProp.Name,
                    Filter = a.ValueKind == JsonValueKind.Object && a.TryGetProperty("filter", out var f)
                        ? JsonHelper.Pretty(f.GetRawText())
                        : "",
                    Routing = a.ValueKind == JsonValueKind.Object
                        ? JsonHelper.GetString(a, "index_routing",
                            JsonHelper.GetString(a, "search_routing", JsonHelper.GetString(a, "routing")))
                        : "",
                });
            }
        }
        return list.OrderBy(x => x.Index, StringComparer.Ordinal)
                   .ThenBy(x => x.Name, StringComparer.Ordinal)
                   .ToList();
    }

    private static string JoinStringArray(JsonElement el, string name)    {
        if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return "";
        return string.Join(", ", arr.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()));
    }

    private static int GetInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;

    private static long GetLong(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : 0;

    private static double GetDouble(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0;
}