using System.Text.Json;

namespace ElasticDesktopManager.Core.Models;

/// <summary>分词结果中的一个 token（对应 _analyze 响应的 tokens 数组元素）。</summary>
public class AnalyzeToken
{
    public string Token { get; set; } = "";
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public string Type { get; set; } = "";
    public int Position { get; set; }

    /// <summary>界面直接展示的位置区间文本。</summary>
    public string RangeText => $"{StartOffset}-{EndOffset}";
}

/// <summary>索引模板 / 组件模板的一条记录。</summary>
public class EsTemplate
{
    public string Name { get; set; } = "";
    /// <summary>可组合索引模板的 composed_of（组件模板列表），逗号拼接。</summary>
    public string ComposedOf { get; set; } = "";
    /// <summary>索引模式（index_patterns），逗号拼接。</summary>
    public string IndexPatterns { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Version { get; set; } = "";
    /// <summary>该模板的完整 JSON（详情展示用）。</summary>
    public string BodyJson { get; set; } = "";
}

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
