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

/// <summary>
/// SLM 自动快照策略（GET /_slm/policy 里的一项）。
/// 注意：SLM 属于 x-pack 功能，OpenSearch 等衍生发行版没有该接口。
/// </summary>
public class EsSlmPolicy
{
    public string PolicyId { get; set; } = "";

    /// <summary>快照名模板，如 &lt;daily-snap-{now/d}&gt;。</summary>
    public string SnapshotNameTemplate { get; set; } = "";

    /// <summary>cron 表达式（7 个字段）。</summary>
    public string Schedule { get; set; } = "";

    public string Repository { get; set; } = "";

    /// <summary>策略覆盖的索引（逗号拼接，默认 *）。</summary>
    public string Indices { get; set; } = "";

    /// <summary>保留策略摘要，如 "30d · keep 5–50"。</summary>
    public string RetentionText { get; set; } = "";

    /// <summary>下次执行时间（本地时间；取不到为空）。</summary>
    public string NextExecution { get; set; } = "";

    /// <summary>最近一次成功（时间 + 快照名）。</summary>
    public string LastSuccess { get; set; } = "";

    /// <summary>最近一次失败（时间 + 原因）。</summary>
    public string LastFailure { get; set; } = "";

    /// <summary>统计摘要，如 "taken 12 · failed 1"。</summary>
    public string StatsText { get; set; } = "";

    /// <summary>策略原文（格式化 JSON，详情展示用）。</summary>
    public string PolicyJson { get; set; } = "";

    public bool HasFailure => !string.IsNullOrEmpty(LastFailure);
}

/// <summary>ILM 生命周期策略（GET /_ilm/policy 里的一项）。</summary>
public class EsIlmPolicy
{
    public string PolicyId { get; set; } = "";

    /// <summary>阶段链，如 "hot → warm → delete"。</summary>
    public string PhasesText { get; set; } = "";

    public string ModifiedDate { get; set; } = "";

    public int IndicesInUseCount { get; set; }

    /// <summary>正在使用该策略的索引（截断展示）。</summary>
    public string IndicesInUse { get; set; } = "";

    /// <summary>策略原文（格式化 JSON，详情展示用）。</summary>
    public string PolicyJson { get; set; } = "";
}

/// <summary>分片恢复状态的一行（GET /_recovery 里一个分片）。</summary>
public class EsRecoveryShard
{
    public string Index { get; set; } = "";

    public string Shard { get; set; } = "";

    /// <summary>INIT / INDEX / VERIFY_INDEX / TRANSLOG / FINALIZE / DONE。</summary>
    public string Stage { get; set; } = "";

    /// <summary>SNAPSHOT / PEER / EXISTING_STORE / LOCAL_SHARDS。</summary>
    public string Type { get; set; } = "";

    public string Source { get; set; } = "";

    public string Target { get; set; } = "";

    /// <summary>文件恢复百分比，如 "100.0%"。</summary>
    public string FilesPercent { get; set; } = "";

    /// <summary>字节进度，如 "1.2 MB / 3.4 MB"。</summary>
    public string BytesText { get; set; } = "";

    /// <summary>耗时，如 "12 ms"。</summary>
    public string TimeText { get; set; } = "";

    public bool IsDone => Stage == "DONE";
}

