using System.Text.Json;

namespace ElasticDesktopManager.Core.Json;

/// <summary>JSON 工具：美化、解析、序列化。</summary>
public static class JsonHelper
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize(object value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>把任意 JSON 字符串格式化为缩进排版的字符串；失败时原样返回。</summary>
    public static string Pretty(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception)
        {
            return json;
        }
    }

    /// <summary>尝试解析为 JSON，成功返回 true 并输出美化文本；失败返回 false 并原样返回。</summary>
    public static bool TryPretty(string input, out string pretty)
    {
        try
        {
            using var doc = JsonDocument.Parse(input);
            pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            return true;
        }
        catch (Exception)
        {
            pretty = input;
            return false;
        }
    }

    /// <summary>从 JSON 对象按属性名取值，不存在返回 null。</summary>
    public static object? Get(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        return el.TryGetProperty(name, out var prop) ? JsonValueToObject(prop) : null;
    }

    public static object? JsonValueToObject(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => el.GetRawText(),
        };
    }

    public static string GetString(JsonElement el, string name, string fallback = "")
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var prop))
            return prop.ValueKind switch
            {
                JsonValueKind.String => prop.GetString() ?? fallback,
                JsonValueKind.Number => prop.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null or JsonValueKind.Undefined => fallback,
                _ => prop.GetRawText(),
            };
        return fallback;
    }
}