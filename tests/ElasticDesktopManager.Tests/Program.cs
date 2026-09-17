using System.Net;
using System.Text;
using System.Text.Json;
using ElasticDesktopManager.Core;
using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Json;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Core.Services;

// ============================================================
// ElasticDesktopManager.Tests — 轻量控制台断言测试（无第三方依赖）
// ============================================================

int passed = 0, failed = 0;
var failures = new List<string>();

void Test(string name, Action fn)
{
    try
    {
        fn();
        passed++;
        Console.WriteLine($"  PASS  {name}");
    }
    catch (Exception ex)
    {
        failed++;
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"  FAIL  {name} => {ex.Message}");
    }
}

void Eq<T>(T expected, T actual, string context)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"{context}: expected <{expected}> but got <{actual}>");
}

void True(bool cond, string context)
{
    if (!cond) throw new Exception($"{context}: expected true");
}

void False(bool cond, string context)
{
    if (cond) throw new Exception($"{context}: expected false");
}

void Contains(string haystack, string needle, string context)
{
    if (!haystack.Contains(needle, StringComparison.Ordinal))
        throw new Exception($"{context}: expected to contain <{needle}> but was <{haystack}>");
}

// ------------------------------------------------------------
// 1. URL 规整（ConfigProperty.BaseUrl）
// ------------------------------------------------------------
Test("BaseUrl: http 补全", () => Eq("http://localhost:9200/", new ConfigProperty { Servers = "localhost:9200", Scheme = "http" }.BaseUrl(), "http"));
Test("BaseUrl: https（SSL）", () => Eq("https://localhost:9200/", new ConfigProperty { Servers = "localhost:9200", Scheme = "https" }.BaseUrl(), "https"));
Test("BaseUrl: 完整 URL 优先自身协议", () => Eq("https://es.example.com:9200/", new ConfigProperty { Servers = "https://es.example.com:9200", Scheme = "http" }.BaseUrl(), "full url"));
Test("BaseUrl: IP 地址", () => Eq("http://192.168.1.5:9200/", new ConfigProperty { Servers = "192.168.1.5:9200" }.BaseUrl(), "ip"));
Test("BaseUrl: 非法协议回退 http", () => Eq("http://localhost:9200/", new ConfigProperty { Servers = "localhost:9200", Scheme = "ftp" }.BaseUrl(), "fallback"));
Test("BaseUrl: 补尾斜杠", () => Eq("http://localhost:9200/", new ConfigProperty { Servers = "localhost:9200/" }.BaseUrl(), "trailing"));

// ------------------------------------------------------------
// 2. SSL 跳过验证（CreateHandler）
// ------------------------------------------------------------
Test("SSL: 默认不跳过 → 校验回调为空", () =>
{
    using var handler = EsClient.CreateHandler(new ConfigProperty());
    True(handler.ServerCertificateCustomValidationCallback is null, "default callback should be null");
});
Test("SSL: SkipSslVerify=true → 校验回调已安装", () =>
{
    using var handler = EsClient.CreateHandler(new ConfigProperty { SkipSslVerify = true });
    True(handler.ServerCertificateCustomValidationCallback is not null, "skip callback should be set");
});
Test("SSL: SkipSslVerify=false → 校验回调为空", () =>
{
    using var handler = EsClient.CreateHandler(new ConfigProperty { SkipSslVerify = false });
    True(handler.ServerCertificateCustomValidationCallback is null, "no skip callback");
});

// ------------------------------------------------------------
// 3. EsClient 请求（注入 FakeHandler，无网络）
// ------------------------------------------------------------
string Exec(Func<EsClient, Task<string>> fn, ConfigProperty cfg, HttpMessageHandler handler, int timeoutSec = 60)
{
    using var client = new EsClient(cfg, handler, timeoutSec, 120);
    return fn(client).GetAwaiter().GetResult();
}

Test("请求: 路径与基址拼接", () =>
{
    var captured = new List<HttpRequestMessage>();
    var handler = new FakeHandler(req => { captured.Add(req); return Ok("{}"); });
    string body = Exec(c => c.GetClusterHealthAsync(), new ConfigProperty { Servers = "localhost:9200" }, handler);
    Eq("http://localhost:9200/_cluster/health", captured[0].RequestUri!.AbsoluteUri, "uri");
    Eq(HttpMethod.Get, captured[0].Method, "method");
    Eq("{}", body, "body");
});

Test("请求: Basic 认证头", () =>
{
    HttpRequestMessage? captured = null;
    var handler = new FakeHandler(req => { captured = req; return Ok("{}"); });
    Exec(c => c.GetEsInfoAsync(), new ConfigProperty
    {
        Servers = "localhost:9200",
        Security = true,
        Username = "elastic",
        Password = "secret",
    }, handler);
    string expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("elastic:secret"));
    Eq(expected, captured!.Headers.Authorization!.ToString(), "auth header");
});

Test("请求: 不启用安全时不带认证头", () =>
{
    HttpRequestMessage? captured = null;
    var handler = new FakeHandler(req => { captured = req; return Ok("{}"); });
    Exec(c => c.GetEsInfoAsync(), new ConfigProperty { Servers = "localhost:9200" }, handler);
    True(captured!.Headers.Authorization is null, "no auth header");
});

Test("请求: SQL body 含 query 与 fetch_size, 路径带 format=json", () =>
{
    HttpRequestMessage? captured = null;
    string? content = null;
    var handler = new FakeHandler(req =>
    {
        captured = req;
        content = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return Ok("{}");
    });
    Exec(c => c.ExecuteSqlAsync("SELECT * FROM test", 50), new ConfigProperty { Servers = "localhost:9200" }, handler);
    Contains(captured!.RequestUri!.AbsoluteUri, "/_sql?format=json", "sql path");
    Contains(content!, "\"fetch_size\":50", "fetch_size");
    Contains(content!, "SELECT * FROM test", "query text");
});

Test("请求: 错误状态码转为 EsException 并携带错误体", () =>
{
    var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
    {
        Content = new StringContent("{\"error\":\"missing authentication credentials\"}"),
    }));
    try
    {
        Exec(c => c.GetClusterHealthAsync(), new ConfigProperty { Servers = "localhost:9200" }, handler);
        throw new Exception("should have thrown");
    }
    catch (EsException ex)
    {
        Eq(401, ex.StatusCode ?? -1, "status code");
        Contains(ex.Message, "missing authentication credentials", "error body text");
    }
});

Test("请求: 超时转为可读错误", () =>
{
    var handler = new SlowHandler(TimeSpan.FromSeconds(3));
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        Exec(c => c.GetClusterHealthAsync(), new ConfigProperty { Servers = "localhost:9200" }, handler, timeoutSec: 1);
        throw new Exception("should have thrown");
    }
    catch (EsException ex)
    {
        Contains(ex.Message, "超时", "timeout message");
        True(sw.Elapsed < TimeSpan.FromSeconds(2.5), $"should timeout fast, took {sw.Elapsed}");
    }
});

// ------------------------------------------------------------
// 4. 解析器
// ------------------------------------------------------------
Test("解析: 集群健康", () =>
{
    var h = EsParsers.ParseHealth("""{"cluster_name":"es-dev","status":"green","number_of_nodes":2,"number_of_data_nodes":2,"active_primary_shards":10,"active_shards":20,"relocating_shards":1,"initializing_shards":0,"unassigned_shards":0,"number_of_pending_tasks":0,"timed_out":false}""");
    Eq("es-dev", h.ClusterName, "cluster");
    Eq("green", h.Status, "status");
    Eq(2, h.NumberOfNodes, "nodes");
    Eq(20, h.ActiveShards, "shards");
});

Test("解析: 索引列表（点号列名）", () =>
{
    var list = EsParsers.ParseIndices("""[{"index":"logs-2025","health":"green","status":"open","pri":"1","rep":"1","docs.count":"1234","store.size":"45kb","memory.total":"0b","creation.date":"1700000000000","uuid":"abc"}]""");
    Eq(1, list.Count, "count");
    Eq("logs-2025", list[0].Index, "index");
    Eq("1234", list[0].DocsCount, "docs");
    Eq("45kb", list[0].StoreSize, "store");
});

Test("解析: 节点列表", () =>
{
    var list = EsParsers.ParseNodes("""[{"name":"node-1","ip":"127.0.0.1","port":"9300","version":"8.10.0","node.role":"mdi","master":"*","cpu":"5","heap.percent":"12","ram.percent":"30","disk.used_percent":"40","load_1m":"0.5","uptime":"3d","jdk":"21.0.1"}]""");
    Eq("node-1", list[0].Name, "name");
    Eq("mdi", list[0].NodeRole, "role");
    Eq("*", list[0].Master, "master");
});

Test("解析: 分片列表", () =>
{
    var list = EsParsers.ParseShards("""[{"index":"logs","shard":"0","prirep":"p","state":"STARTED","docs":"12","store":"3kb","ip":"127.0.0.1","node":"node-1"}]""");
    Eq("STARTED", list[0].State, "state");
    Eq("p", list[0].PriRep, "prirep");
});

Test("解析: SQL 结果（含 cursor）", () =>
{
    var r = EsParsers.ParseSqlResult("""{"columns":[{"name":"name","type":"keyword"},{"name":"age","type":"long"}],"rows":[["a",1],["b",2]],"cursor":"CURSOR_1","took":5}""");
    Eq(2, r.Columns.Count, "cols");
    Eq("name", r.Columns[0], "col0");
    Eq(2, r.Rows.Count, "rows");
    Eq("a", r.Rows[0][0], "cell");
    True(r.HasCursor, "cursor present");
    Eq("CURSOR_1", r.Cursor, "cursor value");
});

Test("解析: 搜索命中与聚合", () =>
{
    var r = EsParsers.ParseSearchResult("""{"took":12,"timed_out":false,"hits":{"total":{"value":100,"relation":"eq"},"hits":[{"_index":"logs","_id":"1","_score":1.5,"_source":{"msg":"hello","n":7}}]},"aggregations":{"t":{"value":7}}}""");
    Eq(100L, r.TotalHits, "total");
    Eq(1, r.Hits.Count, "hit count");
    Eq("hello", r.Hits[0].Source["msg"], "source msg");
    Eq(1.5, r.Hits[0].Score, "score");
    True(r.Aggregations!.ContainsKey("t"), "aggregations");
});

// ------------------------------------------------------------
// 5. 服务（临时数据目录）
// ------------------------------------------------------------
Test("存储: 配置增删改 + 级联删除", () =>
{
    string dir = MakeTempDir();
    var svc = new ConfigService(Path.Combine(dir, "config.json"));

    var folder = new ConfigProperty { Name = "生产", Type = "folder" };
    svc.Upsert(folder);
    True(!string.IsNullOrEmpty(folder.Id), "folder id assigned");

    var cluster = new ConfigProperty { Name = "es-1", Servers = "10.0.0.1:9200", Scheme = "https", SkipSslVerify = true, ParentId = folder.Id };
    svc.Upsert(cluster);

    var loaded = svc.Load();
    Eq(2, loaded.Count, "count after add");
    var back = loaded.First(x => x.Id == cluster.Id);
    True(back.SkipSslVerify, "skip ssl persisted");
    Eq("https", back.Scheme, "scheme persisted");

    // 更新
    cluster.Name = "es-1-prod";
    svc.Upsert(cluster);
    Eq("es-1-prod", svc.Load().First(x => x.Id == cluster.Id).Name, "update name");

    // 级联删除
    svc.DeleteCascade(folder.Id);
    Eq(0, svc.Load().Count, "cascade delete");

    Directory.Delete(dir, true);
});

Test("存储: 设置默认值与会话往返", () =>
{
    string dir = MakeTempDir();
    var svc = new SettingService(Path.Combine(dir, "settings.json"));
    var def = svc.Load();
    Eq("zh_CN", def.Language, "default lang");
    Eq("light", def.Theme, "default theme");
    def.Theme = "dark";
    def.Timeout = 30;
    svc.Save(def);
    var back = svc.Load();
    Eq("dark", back.Theme, "theme roundtrip");
    Eq(30, back.Timeout, "timeout roundtrip");
    Directory.Delete(dir, true);
});

Test("存储: 历史记录上限 100", () =>
{
    string dir = MakeTempDir();
    var svc = new CommandHistoryService(Path.Combine(dir, "history.json"));
    for (int i = 0; i < 120; i++)
        svc.Add(new CommandHistoryItem { Id = $"id{i}", Method = "GET", Command = $"/_cat/indices?i={i}", CreateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
    var items = svc.Load();
    Eq(100, items.Count, "history capped at 100");
    Eq("id119", items[0].Id, "newest first");
    Directory.Delete(dir, true);
});

// ------------------------------------------------------------
// 6. JSON 工具与 i18n
// ------------------------------------------------------------
Test("JSON: 美化", () =>
{
    True(JsonHelper.TryPretty("{\"a\":1}", out var pretty), "valid json");
    Contains(pretty, "\n", "indented");
    Eq("{\"a\":1}", JsonHelper.Pretty("{\"a\":1}")?.Replace("\n", "").Replace(" ", ""), "pretty roundtrip");
    Eq("not-json", JsonHelper.Pretty("not-json"), "invalid passthrough");
});

Test("i18n: zh_CN 默认与 en 切换", () =>
{
    Localization.SetLanguage("zh_CN");
    Eq("索引", Localization.L("nav.indices"), "zh");
    Localization.SetLanguage("en");
    Eq("Indices", Localization.L("nav.indices"), "en");
    Eq("nav.missing.key", Localization.L("nav.missing.key"), "fallback to key");
    Localization.SetLanguage("zh_CN");
});

Test("i18n: 格式化", () =>
{
    Eq("共 100 行", Localization.L("sql.rows", 100), "fmt zh");
    Localization.SetLanguage("en");
    Eq("100 rows", Localization.L("sql.rows", 100), "fmt en");
    Localization.SetLanguage("zh_CN");
});

// ------------------------------------------------------------
// 7. 查询体构建（update/delete by query）与存储健壮性
// ------------------------------------------------------------
Test("EsQueryHelper: update body 含 script（P0 修复回归）", () =>
{
    string dsl = """{"query":{"bool":{"must":[{"term":{"status":"open"}}]}}}""";
    string body = EsQueryHelper.BuildUpdateByQueryBody(dsl, "ctx._source['tag']='done'");
    using var doc = JsonDocument.Parse(body);
    var r = doc.RootElement;
    True(r.TryGetProperty("query", out _), "query present");
    True(r.TryGetProperty("script", out var s), "script present");
    Eq("ctx._source['tag']='done'", JsonHelper.GetString(s, "source"), "script source");
    Eq("painless", JsonHelper.GetString(s, "lang"), "script lang");
    // 保留原 query 结构
    Contains(body, "\"term\"", "term preserved");
});

Test("EsQueryHelper: 无 script 时不带 script 字段", () =>
{
    string body = EsQueryHelper.BuildUpdateByQueryBody("""{"query":{"match_all":{}}}""", null);
    using var doc = JsonDocument.Parse(body);
    True(!doc.RootElement.TryGetProperty("script", out _), "no script key");
});

Test("EsQueryHelper: ExtractQueryPart 只保留 query", () =>
{
    string body = EsQueryHelper.ExtractQueryPart("""{"query":{"term":{"a":"b"}},"track_total_hits":true,"timeout":"30s"}""");
    using var doc = JsonDocument.Parse(body);
    True(doc.RootElement.TryGetProperty("query", out _), "query present");
    True(!doc.RootElement.TryGetProperty("timeout", out _), "no timeout");
    Contains(body, "\"term\"", "term preserved");
});

Test("EsQueryHelper: 非法 DSL 回退 match_all", () =>
{
    string body = EsQueryHelper.BuildUpdateByQueryBody("not-json", "ctx._source['x']=1");
    Contains(body, "match_all", "match_all fallback");
});

Test("存储: 损坏配置文件备份而非静默清空（P2-7 回归）", () =>
{
    string dir = MakeTempDir();
    string file = Path.Combine(dir, "config.json");
    File.WriteAllText(file, "{corrupt json!!");
    var svc = new ConfigService(file);
    var items = svc.Load();
    Eq(0, items.Count, "corrupt -> empty list");
    // 保存不应覆盖原文件，损坏文件应被备份
    svc.Upsert(new ConfigProperty { Name = "new-one", Servers = "x:9200" });
    True(File.Exists(file + ".corrupt") || Directory.GetFiles(dir, "*.corrupt-*").Length > 0, "corrupt backup exists");
    Eq(1, svc.Load().Count, "new data saved alongside backup");
    Directory.Delete(dir, true);
});

Test("存储: 原子写（临时文件+移动）", () =>
{
    string dir = MakeTempDir();
    string file = Path.Combine(dir, "h.json");
    var svc = new CommandHistoryService(file);
    svc.Add(new CommandHistoryItem { Id = "1", Method = "GET", Command = "/_cat/indices", CreateTime = "now" });
    True(File.Exists(file), "file created");
    True(!File.Exists(file + ".tmp"), "no tmp leftover");
    Eq(1, svc.Load().Count, "roundtrip");
    Directory.Delete(dir, true);
});

// ------------------------------------------------------------
// 8. 通用
// ------------------------------------------------------------
Test("i18n: validate.required 格式化", () =>
{
    Localization.SetLanguage("zh_CN");
    Eq("名称 不能为空", Localization.L("validate.required", "名称"), "zh required");
    Localization.SetLanguage("en");
    Eq("Name cannot be empty", Localization.L("validate.required", "Name"), "en required");
    Eq("Delete", Localization.L("common.delete"), "en delete");
    Localization.SetLanguage("zh_CN");
});

Test("BaseUrl: 含协议前缀的服务器地址展示不重复协议（P2-9 回归）", () =>
{
    var cfg = new ConfigProperty { Servers = "https://es.example.com:9200", Scheme = "http" };
    Eq("https://es.example.com:9200", cfg.DisplayServerUrl(), "display url no double scheme");
});

Test("连接树显示文本：HTTPS+完整URL 不出现 https://https://（新增集群不显示回归）", () =>
{
    // 复现用户场景：协议选 HTTPS，地址栏填 https://192.168.5.18
    // ConnectionTreeNode.ServerText 直接委托 DisplayServerUrl()，故此处校验该共享逻辑。
    var cfg = new ConfigProperty { Name = "nls", Servers = "https://192.168.5.18", Scheme = "https" };
    Eq("https://192.168.5.18", cfg.DisplayServerUrl(), "no doubled scheme");
    True(!cfg.DisplayServerUrl().Contains("https://https://"), "must not double");
});

Test("连接树显示文本：纯 host:port 仍按所选协议补全", () =>
{
    Eq("https://192.168.5.18:9200",
        new ConfigProperty { Servers = "192.168.5.18:9200", Scheme = "https" }.DisplayServerUrl(),
        "bare host:port gets scheme");
});

Test("新增集群：保存后可被 Load 读回且能出现在树根（回归）", () =>
{
    string dir = Path.Combine(Path.GetTempPath(), "edm-save-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var svc = new ConfigService(Path.Combine(dir, "config.json"));

    var cfg = new ConfigProperty
    {
        Name = "nls", Servers = "https://192.168.5.18", Scheme = "https",
        Security = true, Username = "nuctech", Password = "pwd",
        SkipSslVerify = true, Type = "cluster", ParentId = ""
    };
    cfg.Id = ""; // 新建时 _item.Id 为空
    svc.Upsert(cfg);
    True(cfg.Id.Length > 0, "id assigned");

    var all = svc.Load();
    Eq("1", all.Count.ToString(), "one item persisted");
    Eq("nls", all[0].Name, "name roundtrip");
    Eq("true", all[0].SkipSslVerify.ToString().ToLowerInvariant(), "skipSsl roundtrip");
    Eq("1", all.Count(x => string.IsNullOrEmpty(x.ParentId)).ToString(), "appears as tree root");
    Directory.Delete(dir, true);
});

Test("请求: GET 携带请求体不被丢弃（P2-6 回归）", () =>
{
    HttpRequestMessage? captured = null;
    string? content = null;
    var handler = new FakeHandler(req =>
    {
        captured = req;
        content = req.Content is null ? null : req.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return Ok("{}");
    });
    Exec(c => c.ExecuteRestAsync("GET", "/_search", "{\"query\":{\"match_all\":{}}}"), new ConfigProperty { Servers = "localhost:9200" }, handler);
    True(captured!.Content is not null, "GET body attached");
    Contains(content!, "match_all", "body content");
});

// ============================================================
// ES 查询示例（REST 页「ES 查询示例」功能）
// ============================================================

Test("查询示例: 目录非空且含 term/match/range 常用查询", () =>
{
    var examples = EsQueryExampleCatalog.Examples;
    True(examples.Count >= 15, $"at least 15 examples, got {examples.Count}");

    var categories = EsQueryExampleCatalog.Categories;
    True(categories.Contains("term"), "term category present");
    True(categories.Contains("match"), "match category present");
    True(categories.Contains("range"), "range category present");
    True(categories.Contains("bool"), "bool category present");
    True(categories.Contains("agg"), "agg category present");
});

Test("查询示例: 每条示例的 method/path 合法、body 为合法 JSON 或空", () =>
{
    foreach (var ex in EsQueryExampleCatalog.Examples)
    {
        True(!string.IsNullOrWhiteSpace(ex.TitleKey), $"{ex.TitleKey}: title key");
        True(!string.IsNullOrWhiteSpace(ex.DescKey), $"{ex.TitleKey}: desc key");
        True(ex.Method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE",
            $"{ex.TitleKey}: valid method, got {ex.Method}");
        True(ex.Path.StartsWith('/'), $"{ex.TitleKey}: path starts with '/', got {ex.Path}");

        if (string.IsNullOrWhiteSpace(ex.Body)) continue;
        // 带 {index} 占位符时先替换再校验，避免占位符处在 JSON 字符串里造成误判
        var materialized = EsQueryExampleCatalog.Materialize(ex, EsQueryExampleCatalog.DefaultIndex).Body;

        // _bulk 是 NDJSON（每行一个 JSON），按行校验而非整体解析
        if (ex.Path == "/_bulk")
        {
            var lines = materialized.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            True(lines.Length >= 2, $"{ex.TitleKey}: bulk has >=2 lines, got {lines.Length}");
            Eq(0, lines.Length % 2, $"{ex.TitleKey}: bulk lines come in pairs");
            foreach (var line in lines)
            {
                try
                {
                    using var _ = JsonDocument.Parse(line);
                }
                catch (Exception e)
                {
                    throw new Exception($"{ex.TitleKey}: bulk line is not valid JSON => {e.Message}");
                }
            }
            continue;
        }

        try
        {
            using var _ = JsonDocument.Parse(materialized);
        }
        catch (Exception e)
        {
            throw new Exception($"{ex.TitleKey}: body is not valid JSON => {e.Message}");
        }
    }
});

Test("查询示例: i18n key 均已在 zh/en 词典中定义", () =>
{
    // L() 查不到时原样返回 key —— 以此判定缺词条；中英都要有
    void AssertTranslated(string key, string what)
    {
        var zh = Localization.L(key);
        if (zh == key || string.IsNullOrWhiteSpace(zh))
            throw new Exception($"{what}: zh_CN 缺少词条 <{key}>");

        Localization.SetLanguage("en");
        var en = Localization.L(key);
        Localization.SetLanguage("zh_CN");
        if (en == key || string.IsNullOrWhiteSpace(en))
            throw new Exception($"{what}: en 缺少词条 <{key}>");
    }

    foreach (var ex in EsQueryExampleCatalog.Examples)
    {
        AssertTranslated(ex.TitleKey, "title");
        AssertTranslated(ex.DescKey, "desc");
    }
    foreach (var cat in EsQueryExampleCatalog.Categories)
        AssertTranslated($"rest.example.cat.{cat}", "category");
});

Test("查询示例: {index} 占位符按索引名替换", () =>
{
    var term = EsQueryExampleCatalog.Examples.First(x => x.TitleKey == "rest.example.term.title");
    var (method, path, body) = EsQueryExampleCatalog.Materialize(term, "my-index");
    Eq("POST", method, "method preserved");
    Eq("/my-index/_search", path, "path index replaced");
    True(!body.Contains("{index}"), "body placeholder replaced");
    Contains(body, "\"term\"", "term clause kept");
});

Test("查询示例: 索引名为空/空白时回退到默认索引名", () =>
{
    var term = EsQueryExampleCatalog.Examples.First(x => x.TitleKey == "rest.example.term.title");
    foreach (var blank in new[] { null, "", "   " })
    {
        var (_, path, _) = EsQueryExampleCatalog.Materialize(term, blank);
        Eq($"/{EsQueryExampleCatalog.DefaultIndex}/_search", path, $"blank index ({blank ?? "null"}) falls back");
    }
});

Test("查询示例: 索引名前后空白被裁剪（不产生非法路径）", () =>
{
    var term = EsQueryExampleCatalog.Examples.First(x => x.TitleKey == "rest.example.term.title");
    var (_, path, _) = EsQueryExampleCatalog.Materialize(term, "  logs-2026  ");
    Eq("/logs-2026/_search", path, "index name trimmed");
});

Test("查询示例: 无占位符的示例（_cat/indices）替换后原样不变", () =>
{
    var cat = EsQueryExampleCatalog.Examples.First(x => x.TitleKey == "rest.example.catIndices.title");
    False(cat.HasIndexPlaceholder, "no placeholder");
    var (method, path, body) = EsQueryExampleCatalog.Materialize(cat, "whatever");
    Eq("GET", method, "method");
    Eq("/_cat/indices?v", path, "path untouched");
    Eq("", body, "no body");
});

Test("查询示例: ByCategory 只返回该分类且保持内置顺序", () =>
{
    var terms = EsQueryExampleCatalog.ByCategory("term");
    True(terms.Count >= 2, $"term has >=2 examples, got {terms.Count}");
    True(terms.All(x => x.Category == "term"), "all in term category");

    // 分类顺序稳定（同一调用两次结果一致）
    var a = EsQueryExampleCatalog.Categories;
    var b = EsQueryExampleCatalog.Categories;
    Eq(string.Join(",", a), string.Join(",", b), "category order stable");
});

Test("查询示例: 每条示例的 TitleKey 唯一（避免界面出现重复项）", () =>
{
    var keys = EsQueryExampleCatalog.Examples.Select(x => x.TitleKey).ToList();
    Eq(keys.Count, keys.Distinct().Count(), "title keys unique");
});

// ------------------------------------------------------------
static string MakeTempDir()
{
    string dir = Path.Combine(Path.GetTempPath(), "edm-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
}

static Task<HttpResponseMessage> Ok(string json)
    => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });

Console.WriteLine();
Console.WriteLine($"===== 结果：通过 {passed}，失败 {failed} =====");
if (failed > 0)
{
    Console.WriteLine("失败明细：");
    foreach (var f in failures) Console.WriteLine("  - " + f);
    Environment.ExitCode = 1;
}

sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _fn;
    public FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> fn) => _fn = fn;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => _fn(request);
}

sealed class SlowHandler : HttpMessageHandler
{
    private readonly TimeSpan _delay;
    public SlowHandler(TimeSpan delay) => _delay = delay;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(_delay, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
    }
}