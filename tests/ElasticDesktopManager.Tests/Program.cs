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

// ============================================================
// 运维/诊断端点（借鉴 ES-King-wails 补齐的能力）
// ============================================================

// 抓取一次请求的 (method, path+query, body)
(string Method, string Uri, string? Body) Capture(Action<EsClient> call, string? baseUrl = null)
{
    HttpRequestMessage? req = null;
    string? content = null;
    var handler = new FakeHandler(r =>
    {
        req = r;
        content = r.Content is null ? null : r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return Ok("{}");
    });
    using var client = new EsClient(
        new ConfigProperty { Servers = baseUrl ?? "localhost:9200" }, handler, 60, 120);
    call(client);
    return (req!.Method.Method, req.RequestUri!.AbsoluteUri, content);
}

// 断言路径中没有双斜杠（协议后的 "//" 除外）——回归：源 Java 项目曾漏 "/"
void NoDoubleSlash(string uri, string context)
{
    var afterScheme = uri[(uri.IndexOf("://", StringComparison.Ordinal) + 3)..];
    False(afterScheme.Contains("//"), $"{context}: 路径出现双斜杠 => {uri}");
    False(afterScheme.Contains("/?"), $"{context}: 路径出现空段 => {uri}");
}

Test("运维: 集群指标 GET /_nodes/stats", () =>
{
    var (m, uri, _) = Capture(c => c.GetNodeStatsAsync().GetAwaiter().GetResult());
    Eq("GET", m, "method");
    True(uri.EndsWith("/_nodes/stats"), $"path => {uri}");
    NoDoubleSlash(uri, "node stats");
});

Test("运维: Mapping 读取与更新", () =>
{
    var (m1, uri1, _) = Capture(c => c.GetMappingAsync("my-index").GetAwaiter().GetResult());
    Eq("GET", m1, "get method");
    True(uri1.EndsWith("/my-index/_mapping"), $"get path => {uri1}");

    var (m2, uri2, body) = Capture(c =>
        c.PutMappingAsync("my-index", "{\"properties\":{\"age\":{\"type\":\"integer\"}}}").GetAwaiter().GetResult());
    Eq("PUT", m2, "put method");
    True(uri2.EndsWith("/my-index/_mapping"), $"put path => {uri2}");
    Contains(body!, "properties", "mapping body forwarded");
    NoDoubleSlash(uri1, "mapping get");
    NoDoubleSlash(uri2, "mapping put");
});

Test("运维: Settings 读取与更新", () =>
{
    var (m, uri, body) = Capture(c =>
        c.PutSettingsAsync("my-index", "{\"index\":{\"number_of_replicas\":2}}").GetAwaiter().GetResult());
    Eq("PUT", m, "method");
    True(uri.EndsWith("/my-index/_settings"), $"path => {uri}");
    Contains(body!, "number_of_replicas", "settings body");
});

Test("运维: 索引名在路径中被正确转义", () =>
{
    var (_, uri, _) = Capture(c => c.GetMappingAsync("weird name/x").GetAwaiter().GetResult());
    True(uri.Contains("weird%20name%2Fx"), $"index escaped => {uri}");
    NoDoubleSlash(uri, "escaped index");
});

Test("运维: 多索引逗号分隔逐个转义但保留逗号", () =>
{
    var (_, uri, _) = Capture(c => c.GetMappingAsync("a b,c d").GetAwaiter().GetResult());
    True(uri.Contains("a%20b,c%20d"), $"multi index escaped => {uri}");
});

Test("运维: 分词 POST /{index}/_analyze 带 field 与 analyzer", () =>
{
    var (m, uri, body) = Capture(c =>
        c.AnalyzeTextAsync("my-index", "hello world", "title", "standard").GetAwaiter().GetResult());
    Eq("POST", m, "method");
    True(uri.EndsWith("/my-index/_analyze"), $"path => {uri}");
    Contains(body!, "hello world", "text in body");
    Contains(body!, "title", "field in body");
    Contains(body!, "standard", "analyzer in body");
});

Test("运维: 分词 不带索引时走 /_analyze，且不写 field 字段", () =>
{
    var (_, uri, body) = Capture(c => c.AnalyzeTextWithBuiltinAsync("hello", "standard").GetAwaiter().GetResult());
    True(uri.EndsWith("/_analyze"), $"path => {uri}");
    False(body!.Contains("\"field\""), "no field key when absent");
    False(body.Contains("\"analyzer\":null"), "no null analyzer");
});

Test("运维: 字段 Top 值聚合自动加 .keyword 且含 cardinality", () =>
{
    var (m, uri, body) = Capture(c =>
        c.FieldTopValuesAsync("my-index", "status", 15).GetAwaiter().GetResult());
    Eq("POST", m, "method");
    True(uri.EndsWith("/my-index/_search"), $"path => {uri}");
    Contains(body!, "status.keyword", "keyword suffix added");
    Contains(body!, "cardinality", "cardinality agg present");
    Contains(body!, "top_values", "terms agg present");
    Contains(body!, "\"size\":0", "size 0 to skip hits");
});

Test("运维: 字段 Top 值 —— 已是 .keyword 时不重复追加", () =>
{
    var (_, _, body) = Capture(c => c.FieldTopValuesAsync("i", "status.keyword").GetAwaiter().GetResult());
    False(body!.Contains("status.keyword.keyword"), "no double suffix");
});

Test("运维: 别名查询与添加（带 filter/routing）", () =>
{
    var (m1, uri1, _) = Capture(c => c.GetAliasesAsync().GetAwaiter().GetResult());
    Eq("GET", m1, "get method");
    True(uri1.EndsWith("/_alias"), $"path => {uri1}");

    var (m2, uri2, body) = Capture(c => c.AddAliasAsync(
        "my-index", "my-alias", "{\"term\":{\"status\":\"active\"}}", "r1").GetAwaiter().GetResult());
    Eq("POST", m2, "add method");
    True(uri2.EndsWith("/_aliases"), $"add path => {uri2}");
    Contains(body!, "\"add\"", "add action");
    Contains(body!, "my-alias", "alias name");
    Contains(body!, "\"routing\":\"r1\"", "routing");
    Contains(body!, "\"filter\"", "filter present");
});

Test("运维: 别名 filter 非法 JSON 时抛错而非发出坏请求", () =>
{
    bool threw = false;
    try
    {
        Capture(c => c.AddAliasAsync("i", "a", "{not json").GetAwaiter().GetResult());
    }
    catch (EsException)
    {
        threw = true;
    }
    True(threw, "invalid filter must throw EsException");
});

Test("运维: Reindex 非法查询 JSON 抛 EsException（不泄漏 JsonException）", () =>
{
    bool threw = false;
    try
    {
        Capture(c => c.ReindexAsync("a", "b", "{not json").GetAwaiter().GetResult());
    }
    catch (EsException)
    {
        threw = true;
    }
    True(threw, "invalid reindex query must throw EsException");
});

Test("运维: 别名移除", () =>
{
    var (m, uri, body) = Capture(c => c.RemoveAliasAsync("my-index", "my-alias").GetAwaiter().GetResult());
    Eq("POST", m, "method");
    True(uri.EndsWith("/_aliases"), $"path => {uri}");
    Contains(body!, "\"remove\"", "remove action");
});

Test("运维: 不传 filter 时不写 filter 字段", () =>
{
    var (_, _, body) = Capture(c => c.AddAliasAsync("i", "a").GetAwaiter().GetResult());
    False(body!.Contains("\"filter\""), "no filter key");
    False(body.Contains("\"routing\""), "no routing key");
});

Test("运维: Reindex 无查询时只含 source/dest", () =>
{
    var (m, uri, body) = Capture(c => c.ReindexAsync("src", "dst").GetAwaiter().GetResult());
    Eq("POST", m, "method");
    True(uri.EndsWith("/_reindex"), $"path => {uri}");
    Contains(body!, "\"src\"", "source");
    Contains(body!, "\"dst\"", "dest");
    False(body!.Contains("\"query\""), "no query key when absent");
});

Test("运维: Reindex 传完整 DSL 时取出 query 部分", () =>
{
    var (_, _, body) = Capture(c => c.ReindexAsync("src", "dst",
        "{\"query\":{\"term\":{\"a\":1}}}").GetAwaiter().GetResult());
    Contains(body!, "\"query\"", "query present");
    Contains(body!, "term", "inner query kept");
    False(body!.Contains("\"source\":{\"index\":\"src\",\"query\":{\"query\""), "no nested query/query");
});

Test("运维: Force Merge 带 max_num_segments", () =>
{
    var (m, uri, _) = Capture(c => c.ForceMergeAsync("my-index", 1).GetAwaiter().GetResult());
    Eq("POST", m, "method");
    True(uri.Contains("/my-index/_forcemerge"), $"path => {uri}");
    True(uri.Contains("max_num_segments=1"), $"query param => {uri}");
    NoDoubleSlash(uri, "forcemerge");
});

Test("运维: 模板 查询/删除/创建", () =>
{
    var (m1, uri1, _) = Capture(c => c.GetIndexTemplatesAsync().GetAwaiter().GetResult());
    Eq("GET", m1, "list method");
    True(uri1.EndsWith("/_index_template"), $"list path => {uri1}");

    var (_, uri2, _) = Capture(c => c.GetComponentTemplatesAsync().GetAwaiter().GetResult());
    True(uri2.EndsWith("/_component_template"), $"component path => {uri2}");

    var (m3, uri3, _) = Capture(c => c.DeleteIndexTemplateAsync("tpl-1").GetAwaiter().GetResult());
    Eq("DELETE", m3, "delete method");
    True(uri3.EndsWith("/_index_template/tpl-1"), $"delete path => {uri3}");

    var (m4, uri4, body) = Capture(c =>
        c.PutIndexTemplateAsync("tpl-1", "{\"index_patterns\":[\"a-*\"]}").GetAwaiter().GetResult());
    Eq("PUT", m4, "put method");
    True(uri4.EndsWith("/_index_template/tpl-1"), $"put path => {uri4}");
    Contains(body!, "index_patterns", "template body");
});

Test("运维: 诊断 分片分配解释 / 热点线程 / 线程池 / 挂起任务", () =>
{
    var (m1, uri1, _) = Capture(c => c.ExplainAllocationAsync("my-index", 0, true).GetAwaiter().GetResult());
    Eq("POST", m1, "explain method");
    True(uri1.EndsWith("/_cluster/allocation/explain"), $"explain path => {uri1}");

    var (m2, uri2, _) = Capture(c => c.HotThreadsAsync().GetAwaiter().GetResult());
    Eq("GET", m2, "hot threads method");
    True(uri2.EndsWith("/_nodes/hot_threads"), $"hot threads path => {uri2}");

    var (m3, uri3, _) = Capture(c => c.HotThreadsAsync("node-1").GetAwaiter().GetResult());
    Eq("GET", m3, "hot threads by node");
    True(uri3.EndsWith("/_nodes/node-1/hot_threads"), $"hot threads node path => {uri3}");

    var (_, uri4, _) = Capture(c => c.GetThreadPoolAsync().GetAwaiter().GetResult());
    True(uri4.EndsWith("/_nodes/thread_pool"), $"thread pool path => {uri4}");

    var (_, uri5, _) = Capture(c => c.GetPendingTasksAsync().GetAwaiter().GetResult());
    True(uri5.EndsWith("/_cluster/pending_tasks"), $"pending tasks path => {uri5}");

    foreach (var u in new[] { uri1, uri2, uri3, uri4, uri5 }) NoDoubleSlash(u, "diag");
});

Test("运维: 全部新增端点路径无漏斜杠/双斜杠（AC3 回归）", () =>
{
    var uris = new List<string>();
    void Cap(Action<EsClient> call) => uris.Add(Capture(call).Uri);

    Cap(c => c.GetNodeStatsAsync().GetAwaiter().GetResult());
    Cap(c => c.GetMappingAsync("i").GetAwaiter().GetResult());
    Cap(c => c.GetSettingsAsync("i").GetAwaiter().GetResult());
    Cap(c => c.AnalyzeTextAsync("i", "t").GetAwaiter().GetResult());
    Cap(c => c.FieldTopValuesAsync("i", "f").GetAwaiter().GetResult());
    Cap(c => c.GetAliasesAsync().GetAwaiter().GetResult());
    Cap(c => c.GetIndexAliasesAsync("i").GetAwaiter().GetResult());
    Cap(c => c.ReindexAsync("a", "b").GetAwaiter().GetResult());
    Cap(c => c.GetIndexTemplatesAsync().GetAwaiter().GetResult());
    Cap(c => c.GetComponentTemplatesAsync().GetAwaiter().GetResult());
    Cap(c => c.ExplainAllocationAsync().GetAwaiter().GetResult());
    Cap(c => c.HotThreadsAsync().GetAwaiter().GetResult());
    Cap(c => c.GetThreadPoolAsync().GetAwaiter().GetResult());
    Cap(c => c.GetPendingTasksAsync().GetAwaiter().GetResult());
    Cap(c => c.ForceMergeAsync("i").GetAwaiter().GetResult());

    foreach (var u in uris) NoDoubleSlash(u, "all new endpoints");
    Eq(15, uris.Count, "endpoint count");
});

// ============================================================
// 指标扁平化（A1 / AC2）
// ============================================================

Test("指标: 深层嵌套被拍平为点分 key", () =>
{
    var rows = EsMetricsFlattener.Flatten(
        "{\"jvm\":{\"mem\":{\"heap_used_in_bytes\":1024}}}");
    Eq(1, rows.Count, "row count");
    Eq("jvm.mem.heap_used_in_bytes", rows[0].Key, "dotted key");
    Eq("jvm", rows[0].Group, "group is first segment");
});

Test("指标: 数组按下标展开而非整块塞入", () =>
{
    var rows = EsMetricsFlattener.Flatten("{\"indices\":{\"docs\":[{\"count\":1},{\"count\":2}]}}");
    Eq(2, rows.Count, "two array elements");
    Eq("indices.docs[0].count", rows[0].Key, "first index key");
    Eq("indices.docs[1].count", rows[1].Key, "second index key");
    Eq("1", rows[0].Value, "first value");
    Eq("2", rows[1].Value, "second value");
});

Test("指标: 分组顺序稳定（同一输入两次结果一致，且已知组按优先级）", () =>
{
    const string json = "{\"os\":{\"cpu\":1},\"jvm\":{\"uptime_in_millis\":5},\"custom_x\":{\"a\":1}}";
    var a = EsMetricsFlattener.Flatten(json);
    var b = EsMetricsFlattener.Flatten(json);

    Eq(string.Join(",", a.Select(r => r.Key)), string.Join(",", b.Select(r => r.Key)), "stable order");

    var groups = EsMetricsFlattener.GroupsOf(a);
    Eq("jvm", groups[0], "jvm ranks before os (priority list)");
    Eq("os", groups[1], "os second");
    Eq("custom_x", groups[2], "unknown group appended last");
});

Test("指标: 字节值格式化为人类可读", () =>
{
    Eq("512 B", EsMetricsFlattener.FormatBytes(512), "bytes");
    Eq("1 KB", EsMetricsFlattener.FormatBytes(1024), "kb");
    Eq("1.5 KB", EsMetricsFlattener.FormatBytes(1536), "fractional kb");
    Eq("1 GB", EsMetricsFlattener.FormatBytes(1024L * 1024 * 1024), "gb");
});

Test("指标: 时长值格式化为可读单位", () =>
{
    Eq("500 ms", EsMetricsFlattener.FormatDuration(500), "ms");
    Eq("1.5 s", EsMetricsFlattener.FormatDuration(1500), "s");
    Eq("2 min", EsMetricsFlattener.FormatDuration(120_000), "min");
    Eq("3 h", EsMetricsFlattener.FormatDuration(3 * 3_600_000), "h");
});

Test("指标: 按 key 语义自动选择格式化（_in_bytes / _in_millis）", () =>
{
    var rows = EsMetricsFlattener.Flatten(
        "{\"jvm\":{\"mem\":{\"heap_used_in_bytes\":2048},\"uptime_in_millis\":1500}}");
    var heap = rows.First(r => r.Key.EndsWith("heap_used_in_bytes", StringComparison.Ordinal));
    var uptime = rows.First(r => r.Key.EndsWith("uptime_in_millis", StringComparison.Ordinal));
    Eq("2 KB", heap.Value, "bytes formatted");
    Eq("1.5 s", uptime.Value, "duration formatted");
});

Test("指标: 布尔与字符串原样展示，空字符串显示为占位", () =>
{
    var rows = EsMetricsFlattener.Flatten("{\"a\":{\"flag\":true,\"name\":\"n1\",\"empty\":\"\"}}");
    Eq("true", rows.First(r => r.Key.EndsWith("flag", StringComparison.Ordinal)).Value, "bool");
    Eq("n1", rows.First(r => r.Key.EndsWith("name", StringComparison.Ordinal)).Value, "string");
    Eq("-", rows.First(r => r.Key.EndsWith("empty", StringComparison.Ordinal)).Value, "empty placeholder");
});

Test("指标: 非法 JSON 返回空列表而不抛异常", () =>
{
    Eq(0, EsMetricsFlattener.Flatten("{not json").Count, "invalid json");
    Eq(0, EsMetricsFlattener.Flatten("").Count, "empty input");
});

Test("指标: _nodes/stats 响应带节点名，多节点各行可区分", () =>
{
    const string json = """
    {"cluster_name":"c1","nodes":{
      "id1":{"name":"node-1","jvm":{"mem":{"heap_used_in_bytes":1024}}},
      "id2":{"name":"node-2","jvm":{"mem":{"heap_used_in_bytes":2048}}}
    }}
    """;
    var rows = EsMetricsFlattener.FlattenNodeStats(json);

    // 每个节点贡献 2 行：name（节点名本身）+ jvm.mem.heap_used_in_bytes
    Eq(2, rows.Count(r => r.Node == "node-1"), "node-1 rows");
    Eq(2, rows.Count(r => r.Node == "node-2"), "node-2 rows");
    True(rows.Any(r => r.Key == "name" && r.Value == "node-1" && r.Node == "node-1"), "node name row");
    True(rows.Any(r => r.Key == "cluster_name" && r.Value == "c1"), "top-level cluster_name kept");
    True(rows.All(r => !r.Key.StartsWith("nodes.", StringComparison.Ordinal)), "nodes wrapper flattened away");
});

Test("指标: 空对象与空数组不被丢弃，显示为空容器标记", () =>
{
    var rows = EsMetricsFlattener.Flatten("{\"a\":{},\"b\":[]}");
    Eq(2, rows.Count, "both kept");
    Eq("{ }", rows.First(r => r.Key == "a").Value, "empty object");
    Eq("[ ]", rows.First(r => r.Key == "b").Value, "empty array");
});

// ============================================================
// 新增解析器：分词 / 模板 / 字段 Top 值
// ============================================================

Test("解析: 分词 tokens（含偏移与位置）", () =>
{
    const string json = """
    {"tokens":[
      {"token":"hello","start_offset":0,"end_offset":5,"type":"<ALPHANUM>","position":0},
      {"token":"world","start_offset":6,"end_offset":11,"type":"<ALPHANUM>","position":1}
    ]}
    """;
    var tokens = EsParsers.ParseAnalyzeTokens(json);
    Eq(2, tokens.Count, "token count");
    Eq("hello", tokens[0].Token, "first token");
    Eq(0, tokens[0].StartOffset, "start offset");
    Eq(5, tokens[0].EndOffset, "end offset");
    Eq("<ALPHANUM>", tokens[0].Type, "type");
    Eq(1, tokens[1].Position, "second position");
    Eq("6-11", tokens[1].RangeText, "range text");
});

Test("解析: 分词 中文分词器结果（多 token、带 offset）", () =>
{
    const string json = """
    {"tokens":[
      {"token":"中华","start_offset":0,"end_offset":2,"type":"CN_WORD","position":0},
      {"token":"人民","start_offset":2,"end_offset":4,"type":"CN_WORD","position":1}
    ]}
    """;
    var tokens = EsParsers.ParseAnalyzeTokens(json);
    Eq(2, tokens.Count, "cn tokens");
    Eq("中华", tokens[0].Token, "cn token text");
    Eq("CN_WORD", tokens[0].Type, "cn token type");
});

Test("解析: 分词 无 tokens 字段时返回空列表而不抛异常", () =>
{
    Eq(0, EsParsers.ParseAnalyzeTokens("{}").Count, "no tokens key");
    Eq(0, EsParsers.ParseAnalyzeTokens("""{"tokens":[]}""").Count, "empty tokens");
});

Test("解析: 索引模板（index_patterns / priority / version，并按名称排序）", () =>
{
    const string json = """
    {
      "zeta": {"index_patterns":["z-*"],"priority":5,"version":2},
      "alpha": {"index_patterns":["a-*","b-*"],"priority":10}
    }
    """;
    var tpl = EsParsers.ParseTemplates(json);
    Eq(2, tpl.Count, "template count");
    Eq("alpha", tpl[0].Name, "sorted by name");
    Eq("a-*, b-*", tpl[0].IndexPatterns, "patterns joined");
    Eq("10", tpl[0].Priority, "priority");
    Eq("", tpl[0].Version, "missing version -> empty");
    Eq("2", tpl[1].Version, "version parsed");
    True(tpl[0].BodyJson.Contains("index_patterns"), "body json kept");
});

Test("解析: 组件模板 composed_of 解析", () =>
{
    const string json = """
    {"comp1":{"template":{"settings":{"number_of_shards":1}},"version":1}}
    """;
    var tpl = EsParsers.ParseTemplates(json);
    Eq(1, tpl.Count, "count");
    Eq("comp1", tpl[0].Name, "name");
    Contains(tpl[0].BodyJson, "number_of_shards", "body");
});

Test("解析: 模板 空对象返回空列表", () =>
{
    Eq(0, EsParsers.ParseTemplates("{}").Count, "empty object");
});

Test("解析: 字段 Top 值（terms buckets + cardinality）", () =>
{
    const string json = """
    {"took":3,"aggregations":{
      "top_values":{"buckets":[
        {"key":"active","doc_count":42},
        {"key":"closed","doc_count":8}
      ]},
      "distinct_count":{"value":7}
    }}
    """;
    var r = EsParsers.ParseFieldTopValues(json);
    Eq(2, r.Values.Count, "bucket count");
    Eq("active", r.Values[0].Value, "first key");
    Eq(42, r.Values[0].Count, "first count");
    Eq(7, r.DistinctCount, "cardinality");
    True(r.Error is null, "no error");
});

Test("解析: 字段 Top 值 数值字段用 key 而非 key_as_string", () =>
{
    const string json = """
    {"aggregations":{"top_values":{"buckets":[{"key":3,"doc_count":5}]},"distinct_count":{"value":2}}}
    """;
    var r = EsParsers.ParseFieldTopValues(json);
    Eq(1, r.Values.Count, "one bucket");
    Eq("3", r.Values[0].Value, "numeric key stringified");
});

Test("解析: 字段 Top 值 ES 返回 error 时保留原因（不抛异常）", () =>
{
    const string json = """
    {"error":{"root_cause":[],"type":"illegal_argument_exception","reason":"Fielddata is disabled on text fields by default"},
     "status":400}
    """;
    var r = EsParsers.ParseFieldTopValues(json);
    Eq(0, r.Values.Count, "no values");
    Contains(r.Error!, "Fielddata is disabled", "error reason surfaced");
});

Test("解析: 字段 Top 值 无 aggregations 时返回空结果", () =>
{
    var r = EsParsers.ParseFieldTopValues("""{"took":1}""");
    Eq(0, r.Values.Count, "no values");
    Eq(0, r.DistinctCount, "no cardinality");
});

Test("运维: 组件模板删除端点路径正确", () =>
{
    var (m, uri, _) = Capture(c => c.DeleteComponentTemplateAsync("comp-1").GetAwaiter().GetResult());
    Eq("DELETE", m, "method");
    True(uri.EndsWith("/_component_template/comp-1"), $"path => {uri}");
    NoDoubleSlash(uri, "component template delete");
});

Test("运维: 分词 指定 field 时才写 field 字段", () =>
{
    var (_, _, body) = Capture(c =>
        c.AnalyzeTextAsync("i", "hello", "title").GetAwaiter().GetResult());
    Contains(body!, "\"field\":\"title\"", "field written");
    False(body!.Contains("\"analyzer\""), "no analyzer key when not given");
});

Test("运维: 字段 Top 值 keyword=false 时不加 .keyword 后缀", () =>
{
    var (_, _, body) = Capture(c =>
        c.FieldTopValuesAsync("i", "age", 10, keyword: false).GetAwaiter().GetResult());
    Contains(body!, "\"age\"", "raw field used");
    False(body!.Contains("age.keyword"), "no suffix");
});

// ============================================================
// 别名解析 + Mapping/Settings 提交体提取（Core 可测）
// ============================================================

Test("解析: 别名（名称/索引/过滤条件/routing）", () =>
{
    const string json = """
    {
      "my-index": {
        "aliases": {
          "alias_all": {},
          "alias_filtered": {
            "filter": { "term": { "status.keyword": "active" } },
            "index_routing": "shard1"
          }
        }
      }
    }
    """;
    var aliases = EsParsers.ParseAliases(json);
    Eq(2, aliases.Count, "alias count");
    Eq("alias_all", aliases[0].Name, "sorted first");
    Eq("my-index", aliases[0].Index, "index name");
    False(aliases[0].HasFilter, "no filter");
    Eq("", aliases[0].Filter, "empty filter");

    var filtered = aliases[1];
    Eq("alias_filtered", filtered.Name, "second alias");
    True(filtered.HasFilter, "has filter");
    Contains(filtered.Filter, "status.keyword", "filter json");
    Eq("shard1", filtered.Routing, "routing");
});

Test("解析: 别名 routing 回退 search_routing → routing", () =>
{
    var bySearch = EsParsers.ParseAliases("""{"i":{"aliases":{"a":{"search_routing":"sr"}}}}""");
    Eq(1, bySearch.Count, "one alias");
    Eq("sr", bySearch[0].Routing, "search_routing fallback");

    var byPlain = EsParsers.ParseAliases("""{"i":{"aliases":{"a":{"routing":"r"}}}}""");
    Eq("r", byPlain[0].Routing, "plain routing fallback");
});

Test("解析: 别名 无 aliases 字段时返回空列表", () =>
{
    Eq(0, EsParsers.ParseAliases("""{"i":{"mappings":{}}}""").Count, "no aliases key");
    Eq(0, EsParsers.ParseAliases("{}").Count, "empty object");
});

Test("Mapping: GET 响应提取出可直接 PUT 的 properties 体", () =>
{
    const string getResponse = """
    {"my-index":{"mappings":{"properties":{"age":{"type":"integer"},"name":{"type":"text"}}}}}
    """;
    var body = EsQueryHelper.ExtractMappingBody(getResponse);
    Contains(body, "\"properties\"", "properties kept");
    Contains(body, "integer", "field type kept");
    False(body.Contains("my-index"), "outer index name removed");
    False(body.Contains("\"mappings\""), "mappings wrapper removed");
});

Test("Mapping: 已是 properties 形态时原样返回（支持读取→编辑→保存循环）", () =>
{
    const string already = """{"properties":{"age":{"type":"integer"}}}""";
    var body = EsQueryHelper.ExtractMappingBody(already);
    Contains(body, "properties", "properties kept");
    Contains(body, "integer", "type kept");
});

Test("Settings: GET 响应提取出可直接 PUT 的 settings 体", () =>
{
    const string getResponse = """
    {"my-index":{"settings":{"index":{"number_of_replicas":"1","number_of_shards":"3"}}}}
    """;
    var body = EsQueryHelper.ExtractSettingsBody(getResponse);
    Contains(body, "number_of_replicas", "setting kept");
    False(body.Contains("my-index"), "outer index removed");
});

Test("Mapping/Settings: 非法 JSON 原样返回（不抛异常）", () =>
{
    Eq("{not json", EsQueryHelper.ExtractMappingBody("{not json"), "mapping passthrough");
    Eq("{not json", EsQueryHelper.ExtractSettingsBody("{not json"), "settings passthrough");
});

Test("运维: 别名解析出的 index 用于删除（多索引场景不串味）", () =>
{
    const string json = """
    {"idx-a":{"aliases":{"shared":{}}},"idx-b":{"aliases":{"shared":{}}}}
    """;
    var aliases = EsParsers.ParseAliases(json);
    Eq(2, aliases.Count, "same alias on two indices");
    Eq("idx-a", aliases[0].Index, "first index");
    Eq("idx-b", aliases[1].Index, "second index");
    Eq("shared", aliases[0].Name, "alias name");
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