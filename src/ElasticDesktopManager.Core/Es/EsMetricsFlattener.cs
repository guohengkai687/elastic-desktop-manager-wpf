using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ElasticDesktopManager.Core.Es;

/// <summary>指标行：扁平化后的一条可展示指标。</summary>
/// <param name="Group">分组键（对应 <c>rest.metrics.group.*</c> i18n key 的后缀）。</param>
/// <param name="Key">原始点分路径，如 <c>jvm.mem.heap_used_in_bytes</c>。</param>
/// <param name="Value">已格式化、可直接展示的值文本。</param>
/// <param name="Node">来源节点名（多节点时用于区分）；单节点汇总时为 null。</param>
public sealed record MetricRow(string Group, string Key, string Value, string? Node = null);

/// <summary>
/// <c>_nodes/stats</c> 响应的扁平化 + 分组 + 值格式化。
///
/// 设计要点（对应 ES-King-wails 的 flattenObject + 前端分组，但改为纯函数以便单测）：
/// 1. 递归拍平嵌套对象；数组按 <c>[i]</c> 展开，避免整块 JSON 塞进一格；
/// 2. 按点分路径的**前缀**归组，顺序稳定（首次出现顺序 = ES 返回的结构顺序）；
/// 3. 值格式化：字节 → 人类可读、毫秒 → 带单位、百分比/比率保留原样、布尔与字符串原样；
/// 4. 全程不依赖任何 UI 类型，Linux 可直接单测。
/// </summary>
public static class EsMetricsFlattener
{
    /// <summary>分组顺序的固定优先级（未列出的分组按出现顺序追加在后面）。</summary>
    private static readonly string[] GroupPriority =
    {
        "cluster", "nodes", "indices", "jvm", "os", "process", "thread_pool",
        "fs", "transport", "http", "breaker", "script", "discovery", "ingest", "adaptive_selection",
    };

    /// <summary>
    /// 拍平并分组 <c>_nodes/stats</c> 响应。
    /// 响应形如 <c>{ "_nodes": {...}, "cluster_name": "x", "nodes": { "&lt;id&gt;": { "name": "...", ...stats } } }</c>。
    /// 每个节点内部的 stats 会被拍平；<c>name</c>/<c>host</c>/<c>transport_address</c> 归入 nodes 组。
    /// </summary>
    public static IReadOnlyList<MetricRow> FlattenNodeStats(string json)
    {
        var rows = new List<MetricRow>();
        if (string.IsNullOrWhiteSpace(json)) return rows;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return rows;
        }

        if (root is not JsonObject obj) return rows;

        // 顶层非 nodes 的标量（cluster_name 等）也展示出来
        foreach (var kv in obj)
        {
            if (kv.Key == "nodes") continue;
            if (kv.Value is JsonValue v && v.GetValueKind() != JsonValueKind.Object)
                rows.Add(new MetricRow("cluster", kv.Key, FormatValue(kv.Key, v)));
        }

        if (obj["nodes"] is not JsonObject nodes) return SortGroups(rows);

        foreach (var nodeEntry in nodes)
        {
            if (nodeEntry.Value is not JsonObject node) continue;
            var nodeName = node["name"] is JsonValue nv && nv.TryGetValue<string>(out var s) ? s : nodeEntry.Key;

            foreach (var kv in node)
            {
                FlattenInto(rows, kv.Key, kv.Value, nodeName, kv.Key);
            }
        }

        return SortGroups(rows);
    }

    /// <summary>拍平任意 JSON 对象（通用入口，便于其它 stats 端点复用）。</summary>
    public static IReadOnlyList<MetricRow> Flatten(string json, string? node = null)
    {
        var rows = new List<MetricRow>();
        if (string.IsNullOrWhiteSpace(json)) return rows;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return rows;
        }

        if (root is JsonObject obj)
        {
            foreach (var kv in obj) FlattenInto(rows, kv.Key, kv.Value, node, kv.Key);
        }
        return SortGroups(rows);
    }

    /// <summary>按分组键取指标（保持稳定顺序）。</summary>
    public static IReadOnlyList<string> GroupsOf(IEnumerable<MetricRow> rows)
        => rows.Select(r => r.Group).Distinct().ToList();

    // ---------- 内部 ----------

    private static void FlattenInto(List<MetricRow> rows, string key, JsonNode? node, string? nodeName, string path)
    {
        switch (node)
        {
            case null:
                rows.Add(new MetricRow(GroupOf(path), path, "-", nodeName));
                break;

            case JsonObject o:
                if (o.Count == 0)
                {
                    rows.Add(new MetricRow(GroupOf(path), path, "{ }", nodeName));
                    break;
                }
                foreach (var kv in o)
                    FlattenInto(rows, kv.Key, kv.Value, nodeName, path + "." + kv.Key);
                break;

            case JsonArray a:
                if (a.Count == 0)
                {
                    rows.Add(new MetricRow(GroupOf(path), path, "[ ]", nodeName));
                    break;
                }
                for (var i = 0; i < a.Count; i++)
                    FlattenInto(rows, key, a[i], nodeName, $"{path}[{i}]");
                break;

            default:
                rows.Add(new MetricRow(GroupOf(path), path, FormatValue(path, node), nodeName));
                break;
        }
    }

    /// <summary>分组键 = 路径首段；路径形如 <c>indices[0].docs.count</c> 时取 <c>indices</c>。</summary>
    private static string GroupOf(string path)
    {
        var first = path.Split('.', '[', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrEmpty(first) ? "other" : first;
    }

    /// <summary>按固定优先级排序：已知分组按 <see cref="GroupPriority"/>，未知分组保持出现顺序接在后面。</summary>
    private static List<MetricRow> SortGroups(List<MetricRow> rows)
    {
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < GroupPriority.Length; i++) order[GroupPriority[i]] = i;

        // 未知分组的顺序 = 其在原始列表中首次出现的次序（保证稳定）
        var unknown = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            if (!order.ContainsKey(r.Group) && !unknown.ContainsKey(r.Group))
                unknown[r.Group] = GroupPriority.Length + unknown.Count;
        }

        int Rank(MetricRow r) => order.TryGetValue(r.Group, out var i)
            ? i
            : unknown.TryGetValue(r.Group, out var j) ? j : int.MaxValue;

        // 稳定排序：同组内保持原始顺序
        return rows.Select((r, idx) => (r, idx))
                   .OrderBy(x => Rank(x.r))
                   .ThenBy(x => x.idx)
                   .Select(x => x.r)
                   .ToList();
    }

    /// <summary>按 key 语义格式化值：字节、毫秒、百分比、时间戳、布尔与其余原样。</summary>
    public static string FormatValue(string key, JsonNode? node)
    {
        if (node is not JsonValue v)
            return node?.ToJsonString() ?? "-";

        if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        if (v.TryGetValue<string>(out var s)) return string.IsNullOrEmpty(s) ? "-" : s;

        if (v.TryGetValue<long>(out var l))
        {
            if (key.EndsWith("_in_millis", StringComparison.Ordinal) ||
                key.EndsWith("_time_in_millis", StringComparison.Ordinal))
                return FormatDuration(l);
            if (key.EndsWith("_in_bytes", StringComparison.Ordinal) ||
                key.EndsWith("_size_in_bytes", StringComparison.Ordinal))
                return FormatBytes(l);
            if (key.EndsWith("_in_seconds", StringComparison.Ordinal) ||
                key.EndsWith("_uptime_in_seconds", StringComparison.Ordinal))
                return FormatDuration(l * 1000);
            return l.ToString(CultureInfo.InvariantCulture);
        }

        if (v.TryGetValue<double>(out var d))
            return d.ToString("0.###", CultureInfo.InvariantCulture);

        return v.ToJsonString();
    }

    /// <summary>字节 → 人类可读（保留 2 位有效小数）。</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return bytes.ToString(CultureInfo.InvariantCulture);
        if (bytes < 1024) return bytes + " B";

        string[] units = { "KB", "MB", "GB", "TB", "PB" };
        double value = bytes;
        var unit = -1;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    /// <summary>毫秒 → 人类可读（ms/s/min/h/d）。</summary>
    public static string FormatDuration(long millis)
    {
        if (millis < 0) return millis.ToString(CultureInfo.InvariantCulture);
        if (millis < 1000) return millis + " ms";

        double seconds = millis / 1000.0;
        if (seconds < 60) return seconds.ToString("0.##", CultureInfo.InvariantCulture) + " s";

        double minutes = seconds / 60;
        if (minutes < 60) return minutes.ToString("0.##", CultureInfo.InvariantCulture) + " min";

        double hours = minutes / 60;
        if (hours < 24) return hours.ToString("0.##", CultureInfo.InvariantCulture) + " h";

        return (hours / 24).ToString("0.##", CultureInfo.InvariantCulture) + " d";
    }
}
