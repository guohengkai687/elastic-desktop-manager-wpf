namespace ElasticDesktopManager.Core.Es;

/// <summary>一条 ES 查询示例：可直接回填到 REST 页的方法 / 路径 / 请求体。</summary>
public sealed class EsQueryExample
{
    /// <summary>分类键（用于界面分组），如 term / match / range。</summary>
    public string Category { get; init; } = "";

    /// <summary>示例标题（i18n key，界面按当前语言解析）。</summary>
    public string TitleKey { get; init; } = "";

    /// <summary>一句话说明（i18n key）。</summary>
    public string DescKey { get; init; } = "";

    /// <summary>HTTP 方法。</summary>
    public string Method { get; init; } = "GET";

    /// <summary>请求路径，可含 {index} 占位符。</summary>
    public string Path { get; init; } = "";

    /// <summary>请求体 JSON，可含 {index} 占位符；GET 类无体示例为空串。</summary>
    public string Body { get; init; } = "";

    /// <summary>是否需要在应用前替换 {index} 占位符。</summary>
    public bool HasIndexPlaceholder =>
        Path.Contains(EsQueryExampleCatalog.IndexPlaceholder, StringComparison.Ordinal) ||
        Body.Contains(EsQueryExampleCatalog.IndexPlaceholder, StringComparison.Ordinal);
}

/// <summary>
/// 内置的常用 ES 查询示例集合。
/// 纯数据 + 纯函数，不依赖任何 UI，便于在 Linux 上直接单测。
/// </summary>
public static class EsQueryExampleCatalog
{
    /// <summary>索引名占位符。</summary>
    public const string IndexPlaceholder = "{index}";

    /// <summary>默认替换索引名（与 SQL 页默认语句保持一致的示例名）。</summary>
    public const string DefaultIndex = "index_name";

    private static readonly List<EsQueryExample> All = new()
    {
        // ---------- 全文 / 匹配 ----------
        new EsQueryExample
        {
            Category = "match",
            TitleKey = "rest.example.match.title",
            DescKey = "rest.example.match.desc",
            Method = "POST",
            Path = "/{index}/_search",
            Body = """
            {
      "query": {
        "match": {
          "message": "hello world"
        }
      },
      "from": 0,
      "size": 10
    }
    """,
        },
        new EsQueryExample
        {
            Category = "match",
            TitleKey = "rest.example.matchPhrase.title",
            DescKey = "rest.example.matchPhrase.desc",
            Method = "POST",
            Path = "/{index}/_search",
            Body = """
            {
      "query": {
        "match_phrase": {
          "message": "hello world"
        }
      },
      "size": 10
    }
    """,
        },
        new EsQueryExample
        {
            Category = "match",
            TitleKey = "rest.example.multiMatch.title",
            DescKey = "rest.example.multiMatch.desc",
            Method = "POST",
            Path = "/{index}/_search",
            Body = """
            {
      "query": {
        "multi_match": {
          "query": "hello world",
          "fields": ["title", "message^2", "content"]
        }
      },
      "size": 10
    }
    """,
        },

        // ---------- 精确匹配 ----------
        new EsQueryExample
        {
            Category = "term",
            TitleKey = "rest.example.term.title",
            DescKey = "rest.example.term.desc",
            Method = "POST",
            Path = "/{index}/_search",
            Body = """
            {
      "query": {
        "term": {
          "status.keyword": "active"
        }
      },
      "size": 10
    }
    """,
        },
        new EsQueryExample
        {
            Category = "term",
            TitleKey = "rest.example.terms.title",
            DescKey = "rest.example.terms.desc",
            Method = "POST",
            Path = "/{index}/_search",
            Body = """
            {
      "query": {
        "terms": {
          "status.keyword": ["active", "pending", "closed"]
        }
      },
      "size": 10
    }
    """,
        },
        new EsQueryExample
        {
            Category = "term",
            TitleKey = "rest.example.ids.title",
            DescKey = "rest.example.ids.desc",
            Method = "POST",
            Path = "/{index}/_search",
            Body = """
            {
      "query": {
        "ids": {
          "values": ["1", "2", "3"]
        }
      }
    }
    """,
        },
        new EsQueryExample
        {
            Category = "term",
            TitleKey = "rest.example.exists.title",
            DescKey = "rest.example.exists.desc",
            Method = "POST",
            Path = "/{index}/_search",
            Body = """
            {
      "query": {
        "exists": {
          "field": "message"
        }
      },
      "size": 10
    }
    """,
        },

        // ---------- 范围 ----------
        new EsQueryExample
        {
            Category = "range",
            TitleKey = "rest.example.range.title",
            DescKey = "rest.example.range.desc",
            Method = "POST",
            Path = "/{index}/_search",
            Body = """
            {
      "query": {
        "range": {
          "age": {
            "gte": 18,
            "lte": 65
          }
        }
      },
      "size": 10
    }
    """,
        },
        new EsQueryExample
        {
            Category = "range",
            TitleKey = "rest.example.rangeDate.title",
            DescKey = "rest.example.rangeDate.desc",
            Method = "POST",
            Path = "/{index}/_search",
            Body = """
            {
      "query": {
        "range": {
          "@timestamp": {
            "gte": "now-7d/d",
            "lte": "now/d",
            "format": "strict_date_optional_time"
          }
        }
      },
      "size": 10,
      "sort": [{ "@timestamp": "desc" }]
    }
    """,
        },

        // ---------- 组合 ----------
        new EsQueryExample
        {
            Category = "bool",
            TitleKey = "rest.example.bool.title",
            DescKey = "rest.example.bool.desc",
            Method = "POST",
            Path = "/{index}/_search",
            Body = """
            {
      "query": {
        "bool": {
          "must": [
            { "match": { "title": "elasticsearch" } }
          ],
          "filter": [
            { "term": { "status.keyword": "active" } },
            { "range": { "age": { "gte": 18 } } }
          ],
          "must_not": [
            { "term": { "deleted": true } }
          ],
          "should": [
            { "match": { "tags": "hot" } }
          ],
          "minimum_should_match": 0
        }
      },
      "size": 10
    }
    """,
        },
        new EsQueryExample
        {
            Category = "bool",
            TitleKey = "rest.example.wildcard.title",
            DescKey = "rest.example.wildcard.desc",
            Method = "POST",
            Path = "/{index}/_search",
            Body = """
            {
      "query": {
        "wildcard": {
          "user.keyword": {
            "value": "zhang*"
          }
        }
      },
      "size": 10
    }
    """,
        },

        // ---------- 排序 / 分页 ----------
        new EsQueryExample
        {
            Category = "sort",
            TitleKey = "rest.example.sort.title",
            DescKey = "rest.example.sort.desc",
            Method = "POST",
            Path = "/{index}/_search",
            Body = """
            {
      "query": { "match_all": {} },
      "sort": [
        { "createTime": { "order": "desc" } },
        { "_score": "desc" }
      ],
      "from": 0,
      "size": 20
    }
    """,
        },
        new EsQueryExample
        {
            Category = "sort",
            TitleKey = "rest.example.sourceFilter.title",
            DescKey = "rest.example.sourceFilter.desc",
            Method = "POST",
            Path = "/{index}/_search",
            Body = """
            {
      "query": { "match_all": {} },
      "_source": ["id", "title", "createTime"],
      "size": 20
    }
    """,
        },

        // ---------- 聚合 ----------
        new EsQueryExample
        {
            Category = "agg",
            TitleKey = "rest.example.aggTerms.title",
            DescKey = "rest.example.aggTerms.desc",
            Method = "POST",
            Path = "/{index}/_search",
            Body = """
            {
      "size": 0,
      "aggs": {
        "by_status": {
          "terms": {
            "field": "status.keyword",
            "size": 20
          }
        }
      }
    }
    """,
        },
        new EsQueryExample
        {
            Category = "agg",
            TitleKey = "rest.example.aggStats.title",
            DescKey = "rest.example.aggStats.desc",
            Method = "POST",
            Path = "/{index}/_search",
            Body = """
            {
      "size": 0,
      "aggs": {
        "age_stats": {
          "stats": { "field": "age" }
        },
        "by_status": {
          "terms": { "field": "status.keyword" },
          "aggs": {
            "avg_age": { "avg": { "field": "age" } }
          }
        }
      }
    }
    """,
        },

        // ---------- 写入 / 更新 / 删除 ----------
        new EsQueryExample
        {
            Category = "write",
            TitleKey = "rest.example.indexDoc.title",
            DescKey = "rest.example.indexDoc.desc",
            Method = "POST",
            Path = "/{index}/_doc",
            Body = """
            {
      "title": "hello",
      "status": "active",
      "age": 20,
      "createTime": "2026-01-01T00:00:00Z"
    }
    """,
        },
        new EsQueryExample
        {
            Category = "write",
            TitleKey = "rest.example.updateDoc.title",
            DescKey = "rest.example.updateDoc.desc",
            Method = "POST",
            Path = "/{index}/_update/1",
            Body = """
            {
      "doc": {
        "status": "closed"
      }
    }
    """,
        },
        new EsQueryExample
        {
            Category = "write",
            TitleKey = "rest.example.bulk.title",
            DescKey = "rest.example.bulk.desc",
            Method = "POST",
            Path = "/_bulk",
            Body = """
            { "index": { "_index": "index_name", "_id": "1" } }
    { "title": "doc 1", "status": "active" }
    { "index": { "_index": "index_name", "_id": "2" } }
    { "title": "doc 2", "status": "closed" }
    """,
        },
        new EsQueryExample
        {
            Category = "write",
            TitleKey = "rest.example.deleteByQuery.title",
            DescKey = "rest.example.deleteByQuery.desc",
            Method = "POST",
            Path = "/{index}/_delete_by_query",
            Body = """
            {
      "query": {
        "term": {
          "status.keyword": "closed"
        }
      }
    }
    """,
        },

        // ---------- 索引管理 ----------
        new EsQueryExample
        {
            Category = "manage",
            TitleKey = "rest.example.catIndices.title",
            DescKey = "rest.example.catIndices.desc",
            Method = "GET",
            Path = "/_cat/indices?v",
            Body = "",
        },
        new EsQueryExample
        {
            Category = "manage",
            TitleKey = "rest.example.getMapping.title",
            DescKey = "rest.example.getMapping.desc",
            Method = "GET",
            Path = "/{index}/_mapping",
            Body = "",
        },
    };

    /// <summary>全部示例（只读）。</summary>
    public static IReadOnlyList<EsQueryExample> Examples => All;

    /// <summary>按分类键取示例（保持内置顺序）。</summary>
    public static IReadOnlyList<EsQueryExample> ByCategory(string category)
        => All.Where(x => x.Category == category).ToList();

    /// <summary>内置分类顺序（界面分组顺序）。</summary>
    public static IReadOnlyList<string> Categories
        => All.Select(x => x.Category).Distinct().ToList();

    /// <summary>
    /// 把示例中的 {index} 占位符替换为指定索引名。
    /// indexName 为空时使用 <see cref="DefaultIndex"/>；索引名不会被空串替换成非法路径。
    /// </summary>
    public static (string Method, string Path, string Body) Materialize(EsQueryExample example, string? indexName)
    {
        ArgumentNullException.ThrowIfNull(example);
        var index = string.IsNullOrWhiteSpace(indexName) ? DefaultIndex : indexName.Trim();
        return (
            example.Method,
            example.Path.Replace(IndexPlaceholder, index, StringComparison.Ordinal),
            example.Body.Replace(IndexPlaceholder, index, StringComparison.Ordinal));
    }
}
