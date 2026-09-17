using System.Text.Json;

namespace ElasticDesktopManager.Core.Models;

/// <summary>索引别名。</summary>
public class EsAlias
{
    public string Name { get; set; } = "";
    public string Index { get; set; } = "";
    /// <summary>别名过滤条件（filter）的 JSON；无 filter 时为空串。</summary>
    public string Filter { get; set; } = "";
    public string Routing { get; set; } = "";

    /// <summary>是否有过滤条件（界面据此显示标记）。</summary>
    public bool HasFilter => !string.IsNullOrEmpty(Filter);
}

/// <summary>字段 Top 值聚合的一行。</summary>
public class FieldTopValue
{
    public string Value { get; set; } = "";
    public long Count { get; set; }
}

/// <summary>字段 Top 值聚合结果：Top 列表 + 去重总数（cardinality）。</summary>
public class FieldTopValuesResult
{
    public List<FieldTopValue> Values { get; set; } = new();
    public long DistinctCount { get; set; }
    /// <summary>未命中聚合时（如字段为 text 且无 .keyword）ES 会返回错误，这里保留原因。</summary>
    public string? Error { get; set; }
}
