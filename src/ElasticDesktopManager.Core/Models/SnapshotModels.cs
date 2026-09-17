namespace ElasticDesktopManager.Core.Models;

/// <summary>快照仓库（_snapshot 响应里的一项）。</summary>
public class EsSnapshotRepository
{
    public string Name { get; set; } = "";

    /// <summary>仓库类型，常见 fs（共享文件系统）、s3、gcs、azure、url。</summary>
    public string Type { get; set; } = "";

    /// <summary>fs 类型仓库的 settings.location；其它类型为空。</summary>
    public string Location { get; set; } = "";

    /// <summary>完整 settings 的格式化 JSON（详情展示用）。</summary>
    public string SettingsJson { get; set; } = "";

    /// <summary>列表里一行显示的摘要文本。</summary>
    public string Summary => string.IsNullOrEmpty(Location) ? Type : $"{Type} · {Location}";
}

/// <summary>一条快照。</summary>
public class EsSnapshot
{
    public string Name { get; set; } = "";

    /// <summary>SUCCESS / FAILED / PARTIAL / IN_PROGRESS / INCOMPATIBLE。</summary>
    public string State { get; set; } = "";

    /// <summary>包含的索引（逗号拼接，过长时由界面截断）。</summary>
    public string Indices { get; set; } = "";

    public int IndexCount { get; set; }

    /// <summary>开始时间（本地时间，yyyy-MM-dd HH:mm:ss）。</summary>
    public string StartedAt { get; set; } = "";

    /// <summary>耗时（人类可读，如 1.2 s）。</summary>
    public string Duration { get; set; } = "";

    /// <summary>创建该快照的 ES 版本。</summary>
    public string Version { get; set; } = "";

    /// <summary>分片成功/总数，如 "3/3"。</summary>
    public string ShardsText { get; set; } = "";

    /// <summary>失败原因摘要（空表示无失败）。</summary>
    public string Failures { get; set; } = "";

    /// <summary>是否创建成功（供界面着色）。</summary>
    public bool IsSuccess => State == "SUCCESS";

    /// <summary>是否处于进行中/部分完成（供界面着色）。</summary>
    public bool IsPending => State is "IN_PROGRESS" or "PARTIAL" or "STARTED";
}
