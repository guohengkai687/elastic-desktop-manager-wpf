using System.Text.Json.Nodes;

namespace ElasticDesktopManager.Core.Es;

/// <summary>
/// ES 查询体构造辅助：供“按查询更新”等需要把 DSL 查询体与附加载荷组合的场景使用。
/// </summary>
public static class EsQueryHelper
{
    /// <summary>
    /// 生成 _update_by_query 请求体。
    /// queryJson 必须是含 "query" 键的 DSL（如构建器产物）；保留原结构。
    /// script 为空时仅含 query（ES 不会修改文档，调用方应据此禁用更新流程）。
    /// </summary>
    public static string BuildUpdateByQueryBody(string queryJson, string? script)
    {
        var body = new JsonObject();
        var root = TryParse(queryJson);

        body["query"] = root is not null && root["query"] is JsonNode q
            ? q.DeepClone()
            : new JsonObject { ["match_all"] = new JsonObject() };

        if (!string.IsNullOrWhiteSpace(script))
        {
            body["script"] = new JsonObject
            {
                ["source"] = script,
                ["lang"] = "painless",
            };
        }
        return body.ToJsonString();
    }

    /// <summary>仅保留 DSL 的 query 部分（用于 delete_by_query 等场景）。</summary>
    public static string ExtractQueryPart(string dslJson)
    {
        var root = TryParse(dslJson);
        var body = new JsonObject
        {
            ["query"] = root is not null && root["query"] is JsonNode q
                ? q.DeepClone()
                : new JsonObject { ["match_all"] = new JsonObject() },
        };
        return body.ToJsonString();
    }

    /// <summary>
    /// 从 <c>GET /{index}/_mapping</c> 的响应中取出可直接提交给
    /// <c>PUT /{index}/_mapping</c> 的映射体。
    ///
    /// GET 返回形如 <c>{ "&lt;index&gt;": { "mappings": { "properties": {...} } } }</c>，
    /// 而 PUT 只接受 <c>{ "properties": {...} }</c>。若传入的已是 properties 形态则原样返回，
    /// 便于“读取 → 编辑 → 保存”闭环。
    /// </summary>
    public static string ExtractMappingBody(string json)
    {
        var root = TryParse(json);
        if (root is null) return json;
        if (root.ContainsKey("properties")) return root.ToJsonString();

        var first = root.FirstOrDefault().Value as JsonObject;
        return first?["mappings"] is JsonNode mappings ? mappings.ToJsonString() : json;
    }

    /// <summary>
    /// 从 <c>GET /{index}/_settings</c> 的响应中取出可提交给
    /// <c>PUT /{index}/_settings</c> 的设置体（去掉最外层索引名与 "settings" 包装）。
    /// </summary>
    public static string ExtractSettingsBody(string json)
    {
        var root = TryParse(json);
        if (root is null) return json;
        if (root.ContainsKey("index")) return root.ToJsonString();

        var first = root.FirstOrDefault().Value as JsonObject;
        return first?["settings"] is JsonNode settings ? settings.ToJsonString() : json;
    }

    private static JsonObject? TryParse(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (Exception)
        {
            return null;
        }
    }
}