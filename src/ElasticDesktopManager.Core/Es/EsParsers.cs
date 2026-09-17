using System.Text.Json;
using ElasticDesktopManager.Core.I18n;
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
                // relation == "gte" 表示上面这个数字只是下限（关闭 track_total_hits 时 ES 会
                // 返回 {"value":10000,"relation":"gte"}）——界面显示 "10000+" 而不是 "10000"。
                result.TotalHitsIsLowerBound = total.ValueKind == JsonValueKind.Object
                    && string.Equals(JsonHelper.GetString(total, "relation"), "gte", StringComparison.Ordinal);
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

    // ================= 新增：字段 Top 值 / 别名 / 快照 =================

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

    // ================= 快照 =================

    /// <summary>
    /// 解析 _snapshot 响应：<c>{ "&lt;repo&gt;": { "type": "fs", "settings": { "location": "..." } } }</c>
    /// </summary>
    public static List<EsSnapshotRepository> ParseSnapshotRepositories(string json)
    {
        var list = new List<EsSnapshotRepository>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var el = prop.Value;
            string location = "";
            string settingsJson = "";
            if (el.TryGetProperty("settings", out var st) && st.ValueKind == JsonValueKind.Object)
            {
                settingsJson = JsonHelper.Pretty(st.GetRawText());
                location = JsonHelper.GetString(st, "location");
            }

            list.Add(new EsSnapshotRepository
            {
                Name = prop.Name,
                Type = JsonHelper.GetString(el, "type"),
                Location = location,
                SettingsJson = settingsJson,
            });
        }
        return list.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>解析 _snapshot/{repo}/_all 的 snapshots 数组（按开始时间倒序）。</summary>
    public static List<EsSnapshot> ParseSnapshots(string json)
    {
        var list = new List<EsSnapshot>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("snapshots", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var el in arr.EnumerateArray())
        {
            var indices = new List<string>();
            if (el.TryGetProperty("indices", out var idx) && idx.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in idx.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String) indices.Add(item.GetString()!);
                }
            }

            long durationMs = GetLong(el, "duration_in_millis");
            list.Add(new EsSnapshot
            {
                Name = JsonHelper.GetString(el, "snapshot"),
                State = JsonHelper.GetString(el, "state"),
                Indices = string.Join(", ", indices),
                IndexCount = indices.Count,
                StartedAt = FormatEpochMillis(GetLong(el, "start_time_in_millis")),
                Duration = durationMs > 0 ? EsMetricsFlattener.FormatDuration(durationMs) : "",
                Version = JsonHelper.GetString(el, "version"),
                ShardsText = ParseShardSummary(el),
                Failures = ParseSnapshotFailures(el),
            });
        }
        return list.OrderByDescending(x => x.StartedAt, StringComparer.Ordinal).ToList();
    }

    private static string ParseShardSummary(JsonElement el)
    {
        if (!el.TryGetProperty("shards", out var sh) || sh.ValueKind != JsonValueKind.Object) return "";
        long total = GetLong(sh, "total");
        if (total <= 0) return "";
        long ok = GetLong(sh, "successful");
        long failed = GetLong(sh, "failed");
        // 分片统计要拼成一句话展示，而 DataGrid 是按行绑定模型属性的，VM 无法逐行格式化，
        // 因此这里直接用 Core 自带的 Localization（Core 自己的词典，不引入任何 UI 依赖）。
        return failed > 0
            ? Localization.L("snapshot.shards.failed", ok, total, failed)
            : $"{ok}/{total}";
    }

    private static string ParseSnapshotFailures(JsonElement el)
    {
        if (!el.TryGetProperty("failures", out var f) || f.ValueKind != JsonValueKind.Array) return "";
        var parts = new List<string>();
        foreach (var item in f.EnumerateArray())
        {
            string index = JsonHelper.GetString(item, "index");
            string reason = "";
            if (item.TryGetProperty("reason", out var r))
                reason = r.ValueKind == JsonValueKind.String ? r.GetString()! : r.GetRawText();
            parts.Add(string.IsNullOrEmpty(index) ? reason : $"{index}: {reason}");
        }
        return string.Join("; ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>epoch 毫秒 → 本地时间字符串（容错：非法/为 0 时返回空串而非抛异常）。</summary>
    private static string FormatEpochMillis(long millis)
    {
        if (millis <= 0) return "";
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(millis).ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "";
        }
    }

    private static int GetInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;

    private static long GetLong(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : 0;

    private static double GetDouble(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0;

    // ================= 索引名列表（搜索页下拉） =================

    /// <summary>
    /// 解析 _cat/indices?format=json&amp;h=index 的响应（数组，每项形如 {"index":"logs-0001"}）。
    /// 与 <see cref="ParseIndices"/> 分开：这里只关心名字，容忍只有 index 一个字段的瘦响应。
    /// </summary>
    public static List<string> ParseIndexNames(string json)
    {
        var list = new List<string>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            string name = JsonHelper.GetString(el, "index");
            if (!string.IsNullOrEmpty(name)) list.Add(name);
        }
        return list;
    }

    // ================= SLM 自动快照策略 =================

    /// <summary>
    /// 解析 GET /_slm/policy：<c>{ "&lt;policyId&gt;": { "policy": {...}, "last_success": ..., "stats": {...} } }</c>
    /// 兼容 7.x（last_success 是对象、next_execution_millis 是毫秒）与 8.x（ISO 字符串）。
    /// </summary>
    public static List<EsSlmPolicy> ParseSlmPolicies(string json)
    {
        var list = new List<EsSlmPolicy>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var el = prop.Value;
            if (el.ValueKind != JsonValueKind.Object) continue;

            JsonElement pol = default;
            bool hasPolicy = el.TryGetProperty("policy", out pol) && pol.ValueKind == JsonValueKind.Object;

            var item = new EsSlmPolicy
            {
                PolicyId = prop.Name,
                PolicyJson = hasPolicy ? JsonHelper.Pretty(pol.GetRawText()) : JsonHelper.Pretty(el.GetRawText()),
            };

            if (hasPolicy)
            {
                item.SnapshotNameTemplate = JsonHelper.GetString(pol, "name");
                item.Schedule = JsonHelper.GetString(pol, "schedule");
                item.Repository = JsonHelper.GetString(pol, "repository");
                item.Indices = ParseNameArray(pol, "config", "indices");
                item.RetentionText = ParseRetention(pol);
            }

            item.NextExecution = TimestampOf(el, "next_execution_millis", "next_execution");
            item.LastSuccess = ParseRunInfo(el, "last_success");
            item.LastFailure = ParseRunInfo(el, "last_failure");
            item.StatsText = ParseSlmStats(el);

            list.Add(item);
        }
        return list.OrderBy(x => x.PolicyId, StringComparer.Ordinal).ToList();
    }

    /// <summary>解析策略的 retention（expire_after / min_count / max_count）为一行摘要。</summary>
    private static string ParseRetention(JsonElement policy)
    {
        if (!policy.TryGetProperty("retention", out var r) || r.ValueKind != JsonValueKind.Object) return "";

        var parts = new List<string>();
        string expire = JsonHelper.GetString(r, "expire_after");
        if (!string.IsNullOrEmpty(expire)) parts.Add(Localization.L("snapshot.retention.expire", expire));
        int min = GetInt(r, "min_count");
        int max = GetInt(r, "max_count");
        if (min > 0 || max > 0)
        {
            parts.Add(Localization.L("snapshot.retention.keep",
                min > 0 ? min.ToString() : "*", max > 0 ? max.ToString() : "*"));
        }
        return string.Join(" · ", parts);
    }

    /// <summary>last_success / last_failure：7.x 是对象，8.x 是 ISO 字符串，两种都要能显示。</summary>
    private static string ParseRunInfo(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return "";
        if (v.ValueKind == JsonValueKind.String) return FormatIsoDate(v.GetString());
        if (v.ValueKind != JsonValueKind.Object) return "";

        var parts = new List<string>();
        // ES 的 SnapshotInvocationRecord 写的是 time（epoch 毫秒）+ time_string（ISO）
        // —— 伴随字段叫 time_string，不是 time_millis。
        string when = TimestampOf(v, "time", "time_string");
        if (!string.IsNullOrEmpty(when)) parts.Add(when);
        string snap = JsonHelper.GetString(v, "snapshot_name");
        if (!string.IsNullOrEmpty(snap)) parts.Add(snap);
        string details = JsonHelper.GetString(v, "details");
        if (string.IsNullOrEmpty(details))
        {
            // 只有失败态才带 reason 字段
            var reason = v.TryGetProperty("reason", out var rs) && rs.ValueKind == JsonValueKind.String
                ? rs.GetString()! : "";
            details = reason;
        }
        if (!string.IsNullOrEmpty(details)) parts.Add(details);
        return string.Join(" · ", parts);
    }

    private static string ParseSlmStats(JsonElement el)
    {
        if (!el.TryGetProperty("stats", out var s) || s.ValueKind != JsonValueKind.Object) return "";
        var parts = new List<string>();
        long taken = GetLong(s, "snapshots_taken");
        long failed = GetLong(s, "snapshots_failed");
        long deleted = GetLong(s, "snapshots_deleted");
        if (taken > 0) parts.Add(Localization.L("snapshot.stats.taken", taken));
        if (failed > 0) parts.Add(Localization.L("snapshot.stats.failed", failed));
        if (deleted > 0) parts.Add(Localization.L("snapshot.stats.deleted", deleted));
        return string.Join(" · ", parts);
    }

    /// <summary>取毫秒时间戳字段或 ISO 字符串字段，统一成本地时间；都取不到返回空串。</summary>
    private static string TimestampOf(JsonElement el, string millisName, string isoName)
    {
        long millis = GetLong(el, millisName);
        if (millis > 0) return FormatEpochMillis(millis);
        string iso = JsonHelper.GetString(el, isoName);
        return FormatIsoDate(iso);
    }

    /// <summary>ISO-8601 → 本地时间；解析失败时原样返回（宁可显示原值，也不要空）。</summary>
    private static string FormatIsoDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "";
        if (DateTimeOffset.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dto))
        {
            return dto.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        }
        return iso;
    }

    // ================= ILM 生命周期策略 =================

    /// <summary>
    /// ILM 阶段的执行顺序（源自 <c>TimeseriesLifecycleType.ORDERED_VALID_PHASES</c>）。
    /// 未识别的阶段排在已知阶段之后；因为排序是稳定的，多个未知阶段之间仍保持 ES 返回顺序。
    /// </summary>
    private static readonly string[] IlmPhaseOrder = { "hot", "warm", "cold", "frozen", "delete" };

    private static int IlmPhaseRank(string phaseName)
    {
        int i = Array.IndexOf(IlmPhaseOrder, phaseName);
        return i < 0 ? IlmPhaseOrder.Length : i;
    }

    /// <summary>解析 GET /_ilm/policy：<c>{ "&lt;policyId&gt;": { "policy": { "phases": {...} }, "in_use_by": {...} } }</c></summary>
    public static List<EsIlmPolicy> ParseIlmPolicies(string json)
    {
        var list = new List<EsIlmPolicy>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var el = prop.Value;
            if (el.ValueKind != JsonValueKind.Object) continue;

            var item = new EsIlmPolicy
            {
                PolicyId = prop.Name,
                // 真实形状（ES 7.17 / 8.17 / main 的 LifecyclePolicyMetadata 一致）：
                //   modified_date        是 declareLong 的 **epoch 毫秒**，
                //   modified_date_string 才是 ISO 串（getModifiedDateString()）。
                // 早先这里只读 modified_date 并当 ISO 解析，解析失败原样返回 → 每行都显示
                // "1718452800000" 这种裸数字（SLM 的 modified_date 才是 ISO，两边形状不同）。
                ModifiedDate = TimestampOf(el, "modified_date", "modified_date_string"),
                PolicyJson = JsonHelper.Pretty(el.GetRawText()),
            };

            if (el.TryGetProperty("policy", out var pol) && pol.ValueKind == JsonValueKind.Object &&
                pol.TryGetProperty("phases", out var phases) && phases.ValueKind == JsonValueKind.Object)
            {
                var names = new List<string>();
                foreach (var ph in phases.EnumerateObject()) names.Add(ph.Name);
                // 不能直接用 ES 返回的顺序：LifecyclePolicy 的 phases 是 Collectors.toMap 建的
                // HashMap（`LifecyclePolicy.java:58`），toXContent 按 phases.values() 写出
                // （`:202-205`），从集群状态反序列化走 readImmutableMap 时顺序甚至是随机的。
                // 而 UI 用 "→" 把它呈现成执行链，必须按 ILM 的执行顺序重排，
                // 否则会显示成 "warm → delete → hot" 这种误导性链路。
                // 用 OrderBy（稳定排序）而不是 List.Sort：ILM 将来新增的阶段（排名相同）
                // 会保持 ES 返回的先后关系，不会因为排序算法变得不可预期。
                item.PhasesText = string.Join(" → ", names.OrderBy(IlmPhaseRank));
            }

            if (el.TryGetProperty("in_use_by", out var use) && use.ValueKind == JsonValueKind.Object &&
                use.TryGetProperty("indices", out var idx) && idx.ValueKind == JsonValueKind.Array)
            {
                var names = new List<string>();
                foreach (var i in idx.EnumerateArray())
                    if (i.ValueKind == JsonValueKind.String) names.Add(i.GetString()!);
                item.IndicesInUseCount = names.Count;
                item.IndicesInUse = string.Join(", ", names);
            }

            list.Add(item);
        }
        return list.OrderBy(x => x.PolicyId, StringComparer.Ordinal).ToList();
    }

    /// <summary>解析 GET /_recovery：<c>{ "&lt;index&gt;": { "shards": [ ... ] } }</c> → 每个分片一行。</summary>
    public static List<EsRecoveryShard> ParseRecovery(string json)
    {
        var list = new List<EsRecoveryShard>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return list;

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            // 和其他解析器保持一致：先判 Object，否则根成员是字符串/数组时 TryGetProperty 会抛
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            if (!prop.Value.TryGetProperty("shards", out var shards) || shards.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var sh in shards.EnumerateArray())
            {
                if (sh.ValueKind != JsonValueKind.Object) continue;
                var row = new EsRecoveryShard
                {
                    Index = prop.Name,
                    Shard = GetLong(sh, "id").ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Stage = JsonHelper.GetString(sh, "stage"),
                    Type = JsonHelper.GetString(sh, "type"),
                    Source = HostOf(sh, "source"),
                    Target = HostOf(sh, "target"),
                };

                if (sh.TryGetProperty("index", out var idx) && idx.ValueKind == JsonValueKind.Object)
                {
                    if (idx.TryGetProperty("files", out var f) && f.ValueKind == JsonValueKind.Object)
                        row.FilesPercent = JsonHelper.GetString(f, "percent");
                    if (idx.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Object)
                    {
                        long rec = GetLong(s, "recovered_in_bytes");
                        long total = GetLong(s, "total_in_bytes");
                        row.BytesText = total > 0
                            ? $"{EsMetricsFlattener.FormatBytes(rec)} / {EsMetricsFlattener.FormatBytes(total)}"
                            : EsMetricsFlattener.FormatBytes(rec);
                    }
                }

                long ms = GetLong(sh, "total_time_in_millis");
                row.TimeText = ms > 0 ? EsMetricsFlattener.FormatDuration(ms) : "";
                list.Add(row);
            }
        }
        return list;
    }

    private static string HostOf(JsonElement shard, string side)
    {
        if (!shard.TryGetProperty(side, out var s) || s.ValueKind != JsonValueKind.Object) return "";
        string host = JsonHelper.GetString(s, "host");
        if (!string.IsNullOrEmpty(host)) return host;
        string name = JsonHelper.GetString(s, "name");
        if (!string.IsNullOrEmpty(name)) return name;

        // 快照恢复的 source 没有 host/name，只有 repository + snapshot
        // （不带上这一层，恢复页的"来源"列在最主要的场景下永远是空的）。
        string repo = JsonHelper.GetString(s, "repository");
        string snap = JsonHelper.GetString(s, "snapshot");
        if (string.IsNullOrEmpty(repo) && string.IsNullOrEmpty(snap)) return "";
        return string.IsNullOrEmpty(repo) ? snap : $"{repo}/{snap}";
    }

    /// <summary>取 obj[a][b] 里的字符串数组并拼接（缺失返回空串）。</summary>
    private static string ParseNameArray(JsonElement obj, string a, string b)
    {
        if (!obj.TryGetProperty(a, out var inner) || inner.ValueKind != JsonValueKind.Object) return "";
        if (!inner.TryGetProperty(b, out var arr) || arr.ValueKind != JsonValueKind.Array) return "";
        var names = new List<string>();
        foreach (var item in arr.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String) names.Add(item.GetString()!);
        return string.Join(", ", names);
    }
}