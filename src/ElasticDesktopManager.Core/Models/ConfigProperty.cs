using System.Text.Json;

namespace ElasticDesktopManager.Core.Models;

/// <summary>
/// 连接配置项（源项目 ConfigProperty 的等价物）。
/// 新增字段：<see cref="Scheme"/>（http/https —— SSL 支持）与
/// <see cref="SkipSslVerify"/>（跳过 SSL 证书验证）。
/// </summary>
public class ConfigProperty
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>地址：可填 host:port 或完整 URL（如 https://es.example.com:9200）。</summary>
    public string Servers { get; set; } = "";
    /// <summary>协议：http / https（SSL 支持）。若 Servers 已含协议前缀则优先。</summary>
    public string Scheme { get; set; } = "http";
    /// <summary>是否启用安全认证（Basic）。</summary>
    public bool Security { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>跳过 SSL 证书验证（自签名/内网证书时勾选）。</summary>
    public bool SkipSslVerify { get; set; }
    /// <summary>类型：cluster / folder。</summary>
    public string Type { get; set; } = "cluster";
    public string ParentId { get; set; } = "";

    public bool IsFolder => Type == "folder";

    public ConfigProperty Clone()
    {
        return new ConfigProperty
        {
            Id = Id,
            Name = Name,
            Servers = Servers,
            Scheme = Scheme,
            Security = Security,
            Username = Username,
            Password = Password,
            SkipSslVerify = SkipSslVerify,
            Type = Type,
            ParentId = ParentId,
        };
    }

    /// <summary>连接显示名，含协议与跳过验证标记。</summary>
    public string DisplayName => IsFolder ? Name : $"{Name}  ({DisplayServerUrl()})";

    /// <summary>展示用服务器地址：Servers 已含协议前缀时原样展示，否则按 Scheme 拼接。</summary>
    public string DisplayServerUrl()
    {
        var servers = (Servers ?? "").Trim();
        if (servers.Contains("://"))
            return servers;
        var scheme = string.IsNullOrWhiteSpace(Scheme) ? "http" : Scheme.Trim().ToLowerInvariant();
        return $"{scheme}://{servers}";
    }

    /// <summary>规整为完整基地址（如 http://localhost:9200/）。</summary>
    public string BaseUrl()
    {
        string servers = Servers?.Trim() ?? "";
        if (servers.Contains("://"))
        {
            // 用户直接给了完整 URL，以其自身 scheme 为准
            if (!servers.EndsWith("/"))
                servers += "/";
            return servers;
        }
        string scheme = string.IsNullOrWhiteSpace(Scheme) ? "http" : Scheme.Trim().ToLowerInvariant();
        if (scheme is not ("http" or "https"))
            scheme = "http";
        if (!servers.EndsWith("/"))
            servers += "/";
        return $"{scheme}://{servers}";
    }
}