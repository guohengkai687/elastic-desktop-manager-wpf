namespace ElasticDesktopManager.Core.Models;

/// <summary>REST 历史记录项（源 es_command_history 等价物）。</summary>
public class CommandHistoryItem
{
    public string Id { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string Command { get; set; } = "";
    public string? CommandValue { get; set; }
    public string CreateTime { get; set; } = "";
}

/// <summary>索引条目（_cat/indices JSON 行）。</summary>
public class EsIndex
{
    public string Index { get; set; } = "";
    public string Health { get; set; } = "";
    public string Status { get; set; } = "";
    public string Uuid { get; set; } = "";
    public string Pri { get; set; } = "";
    public string Rep { get; set; } = "";
    public string DocsCount { get; set; } = "";
    public string StoreSize { get; set; } = "";
    public string MemoryTotal { get; set; } = "";
    public string CreationDate { get; set; } = "";
}

/// <summary>节点条目（_cat/nodes JSON 行，字段按需取值）。</summary>
public class EsNode
{
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Port { get; set; } = "";
    public string Version { get; set; } = "";
    public string NodeRole { get; set; } = "";
    public string Master { get; set; } = "";
    public string Cpu { get; set; } = "";
    public string HeapPercent { get; set; } = "";
    public string RamPercent { get; set; } = "";
    public string DiskUsedPercent { get; set; } = "";
    public string Load1m { get; set; } = "";
    public string Uptime { get; set; } = "";
    public string Jdk { get; set; } = "";
}

/// <summary>分片条目（_cat/shards JSON 行）。</summary>
public class EsShard
{
    public string Index { get; set; } = "";
    public string Shard { get; set; } = "";
    public string PriRep { get; set; } = "";
    public string State { get; set; } = "";
    public string Docs { get; set; } = "";
    public string Store { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Node { get; set; } = "";
}

/// <summary>集群健康（/_cluster/health）。</summary>
public class EsHealth
{
    public string ClusterName { get; set; } = "";
    public string Status { get; set; } = "";
    public int NumberOfNodes { get; set; }
    public int NumberOfDataNodes { get; set; }
    public int ActivePrimaryShards { get; set; }
    public int ActiveShards { get; set; }
    public int RelocatingShards { get; set; }
    public int InitializingShards { get; set; }
    public int UnassignedShards { get; set; }
    public int DelayedUnassignedShards { get; set; }
    public int NumberOfPendingTasks { get; set; }
    public int NumberOfInFlightFetch { get; set; }
    public double TaskMaxWaitingInQueueMillis { get; set; }
    public bool TimedOut { get; set; }
}

/// <summary>SQL 查询结果（/_sql?format=json）。</summary>
public class EsSqlResult
{
    public List<string> Columns { get; set; } = new();
    public List<List<object?>> Rows { get; set; } = new();
    public string? Cursor { get; set; }
    public int Took { get; set; }
    public bool IsPartial { get; set; }
    public bool IsAsync { get; set; }

    public bool HasCursor => !string.IsNullOrEmpty(Cursor);
}

/// <summary>搜索命中行（从 _source 提取字段）。</summary>
public class EsSearchHit
{
    public string Id { get; set; } = "";
    public string Index { get; set; } = "";
    public double Score { get; set; }
    public Dictionary<string, object?> Source { get; set; } = new();
}

/// <summary>搜索响应汇总。</summary>
public class EsSearchResult
{
    public long TotalHits { get; set; }
    public long Took { get; set; }
    public bool TimedOut { get; set; }
    public List<EsSearchHit> Hits { get; set; } = new();
    public Dictionary<string, object?>? Aggregations { get; set; }
}