using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ElasticDesktopManager.Core;
using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Json;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Core.Services;
using ElasticDesktopManager.Core.Ui;

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
    False(r.TotalHitsIsLowerBound, "relation=eq → 精确计数");
});

Test("解析: hits.total.relation=gte 表示命中数只是下限", () =>
{
    // 关闭 track_total_hits 时 ES 返回 {"value":10000,"relation":"gte"}：
    // 界面必须显示 "10000+"，否则把"至少 1 万"说成"正好 1 万"，分页也会过早禁用下一页。
    var gte = EsParsers.ParseSearchResult(
        """{"hits":{"total":{"value":10000,"relation":"gte"},"hits":[]}}""");
    Eq(10000L, gte.TotalHits, "total");
    True(gte.TotalHitsIsLowerBound, "relation=gte → 下限");

    // ES 7 之前的扁平数字形态（无 relation）→ 视为精确值
    var flat = EsParsers.ParseSearchResult("""{"hits":{"total":2570,"hits":[]}}""");
    Eq(2570L, flat.TotalHits, "扁平 total");
    False(flat.TotalHitsIsLowerBound, "扁平形态没有 relation → 精确值");

    False(EsParsers.ParseSearchResult("""{"hits":{}}""").TotalHitsIsLowerBound, "没有 total → 不是下限");
});

Test("分页: from/size 数学（对齐原版 PagingControl 的行为与上限）", () =>
{
    // from = (页号-1) × 每页条数
    Eq(0, SearchPaging.FromOf(1, 10), "第 1 页 from=0");
    Eq(10, SearchPaging.FromOf(2, 10), "第 2 页 from=10");
    Eq(2560, SearchPaging.FromOf(257, 10), "2570 条命中、每页 10 条 → 第 257 页 from=2560");
    Eq(0, SearchPaging.FromOf(0, 10), "页号 < 1 收敛到 0");
    Eq(0, SearchPaging.FromOf(-5, 10), "负页号收敛到 0");
    Eq(0, SearchPaging.FromOf(3, 0), "每页 0 条不产生负偏移");
    Eq(0, SearchPaging.FromOf(1, -10), "负每页条数不产生负偏移");

    // 总页数（至少 1；整除与不整除）
    Eq(1, SearchPaging.TotalPages(0, 10), "0 条 → 1 页（不显示 第 1/0 页）");
    Eq(1, SearchPaging.TotalPages(10, 10), "正好一页");
    Eq(2, SearchPaging.TotalPages(11, 10), "11 条 → 2 页");
    Eq(257, SearchPaging.TotalPages(2570, 10), "2570 条 / 每页 10 → 257 页");
    Eq(26, SearchPaging.TotalPages(2570, 100), "换每页 100 → 26 页");
    Eq(1, SearchPaging.TotalPages(2570, 0), "每页 0 条 → 兜底 1 页（不除零）");

    // 页码收敛
    Eq(1, SearchPaging.ClampPage(0, 5), "小于 1 → 1");
    Eq(5, SearchPaging.ClampPage(99, 5), "超过总页数 → 最后一页");
    Eq(3, SearchPaging.ClampPage(3, 5), "合法页号不动");

    // 结果窗口上限（ES index.max_result_window 默认 10000；取 5000，见 SearchPaging.MaxFrom 注释）
    False(SearchPaging.ExceedsWindow(501, 10), "第 501 页 from=5000 恰好在上限内");
    True(SearchPaging.ExceedsWindow(502, 10), "第 502 页 from=5010 超限");
    False(SearchPaging.ExceedsWindow(51, 100), "每页 100 时第 51 页 from=5000 仍在上限内");

    // 下一页判定
    True(SearchPaging.HasNext(1, 10, 2570, false), "2570 条第 1 页还有下一页");
    False(SearchPaging.HasNext(257, 10, 2570, false), "最后一页没有下一页");
    False(SearchPaging.HasNext(1, 10, 0, false), "0 条没有下一页");
    False(SearchPaging.HasNext(1, 10, 5, false), "只有 5 条（不足一页）没有下一页");
    True(SearchPaging.HasNext(1, 10, 10000, true), "relation=gte 时按'可能还有'处理");
    False(SearchPaging.HasNext(501, 10, 100000, true), "relation=gte 也不能越过结果窗口上限");
});

Test("分页: 搜索 DSL 注入 from/size（分页只能由服务端完成）", () =>
{
    string dsl = """{"query":{"match_all":{}},"track_total_hits":true,"timeout":"30s"}""";

    string paged = EsQueryHelper.WithPaging(dsl, 20, 10);
    using var doc = JsonDocument.Parse(paged);
    var root = doc.RootElement;
    Eq(20, root.GetProperty("from").GetInt32(), "from 写入");
    Eq(10, root.GetProperty("size").GetInt32(), "size 写入");
    // 原有查询必须原样保留（不能被分页覆盖掉）
    True(root.GetProperty("query").TryGetProperty("match_all", out _), "query 保留");
    True(root.GetProperty("track_total_hits").GetBoolean(), "track_total_hits 保留");

    // 覆盖已有值（不是重复追加）
    string twice = EsQueryHelper.WithPaging(paged, 0, 50);
    using var doc2 = JsonDocument.Parse(twice);
    Eq(0, doc2.RootElement.GetProperty("from").GetInt32(), "from 被覆盖");
    Eq(50, doc2.RootElement.GetProperty("size").GetInt32(), "size 被覆盖");
    Eq(1, System.Text.RegularExpressions.Regex.Matches(twice, "\"from\"").Count, "from 只出现一次");

    // 负值兜底为 0（不能把负数发给 ES）
    using var doc3 = JsonDocument.Parse(EsQueryHelper.WithPaging(dsl, -5, -1));
    Eq(0, doc3.RootElement.GetProperty("from").GetInt32(), "负 from → 0");
    Eq(0, doc3.RootElement.GetProperty("size").GetInt32(), "负 size → 0");

    // 非法 JSON 原样返回（与本文件其它方法一致：交给 ES 报错，不在这里抛）
    Eq("not json", EsQueryHelper.WithPaging("not json", 0, 10), "非法 JSON 原样返回");
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

Test("i18n: 关于窗口署名——作者是本仓库作者，上游署名仍保留", () =>
{
    // 第 8 轮：关于窗口的作者字段沿用了上游 JavaFX 原版的值（lxwise），
    // 且 GitHub 按钮指向的是上游仓库。作者字段由 app.author 词条决定，这里钉死它。
    Localization.SetLanguage("zh_CN");
    Eq("作者：guohengkai", Localization.L("about.author", Localization.L("app.author")), "zh author");
    Localization.SetLanguage("en");
    Eq("Author: guohengkai", Localization.L("about.author", Localization.L("app.author")), "en author");
    // 反面：不能把上游署名一起删掉（上游为 Apache-2.0，署名是许可要求）
    True(Localization.L("about.desc").Contains("lxwise", StringComparison.Ordinal), "upstream credit kept");
    True(Localization.L("about.desc").Contains("Apache-2.0", StringComparison.Ordinal), "upstream license kept");
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
// 运维端点（借鉴 ES-King-wails 补齐的能力）
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
Test("运维: 全部新增端点路径无漏斜杠/双斜杠（AC3 回归）", () =>
{
    var uris = new List<string>();
    void Cap(Action<EsClient> call) => uris.Add(Capture(call).Uri);

    Cap(c => c.GetNodeStatsAsync().GetAwaiter().GetResult());
    Cap(c => c.GetMappingAsync("i").GetAwaiter().GetResult());
    Cap(c => c.GetSettingsAsync("i").GetAwaiter().GetResult());
    Cap(c => c.FieldTopValuesAsync("i", "f").GetAwaiter().GetResult());
    Cap(c => c.GetAliasesAsync().GetAwaiter().GetResult());
    Cap(c => c.GetIndexAliasesAsync("i").GetAwaiter().GetResult());
    Cap(c => c.ReindexAsync("a", "b").GetAwaiter().GetResult());
    Cap(c => c.ForceMergeAsync("i").GetAwaiter().GetResult());

    foreach (var u in uris) NoDoubleSlash(u, "all new endpoints");
    Eq(8, uris.Count, "endpoint count");
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
// 新增解析器：字段 Top 值 / 别名
// ============================================================
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

// ============================================================
// 快照管理（A10）
// ============================================================

Test("快照: 仓库 列表/创建/校验/删除 端点契约", () =>
{
    var (m1, uri1, _) = Capture(c => c.GetSnapshotRepositoriesAsync().GetAwaiter().GetResult());
    Eq("GET", m1, "list method");
    True(uri1.EndsWith("/_snapshot"), $"list path => {uri1}");

    var (m2, uri2, body2) = Capture(c => c.CreateSnapshotRepositoryAsync(
        "backup", """{"type":"fs","settings":{"location":"/mnt/b"}}""").GetAwaiter().GetResult());
    Eq("PUT", m2, "create method");
    True(uri2.EndsWith("/_snapshot/backup"), $"create path => {uri2}");
    Contains(body2!, "\"type\":\"fs\"", "body 原样透传（仓库类型由用户决定，不写死 fs）");

    var (m3, uri3, _) = Capture(c => c.VerifySnapshotRepositoryAsync("backup").GetAwaiter().GetResult());
    Eq("POST", m3, "verify method");
    True(uri3.EndsWith("/_snapshot/backup/_verify"), $"verify path => {uri3}");

    var (m4, uri4, _) = Capture(c => c.DeleteSnapshotRepositoryAsync("backup").GetAwaiter().GetResult());
    Eq("DELETE", m4, "delete method");
    True(uri4.EndsWith("/_snapshot/backup"), $"delete path => {uri4}");

    foreach (var u in new[] { uri1, uri2, uri3, uri4 }) NoDoubleSlash(u, "snapshot repo");
});

Test("快照: 列表/创建/删除/恢复 端点契约（创建与恢复不阻塞）", () =>
{
    var (m1, uri1, _) = Capture(c => c.GetSnapshotsAsync("backup").GetAwaiter().GetResult());
    Eq("GET", m1, "list method");
    True(uri1.EndsWith("/_snapshot/backup/_all"), $"list path => {uri1}");

    var (m2, uri2, body2) = Capture(c => c.CreateSnapshotAsync("backup", "snap-1").GetAwaiter().GetResult());
    Eq("PUT", m2, "create method");
    True(uri2.Contains("/_snapshot/backup/snap-1"), $"create path => {uri2}");
    Contains(uri2, "wait_for_completion=false", "大集群下不能用同步等待挂住请求");
    Contains(body2!, "\"indices\":\"*\"", "未指定索引时视为全部");
    Contains(body2!, "\"include_global_state\":false", "默认不包含全局状态");

    var (m3, uri3, body3) = Capture(c => c.CreateSnapshotAsync("backup", "snap-1", "a,b", true).GetAwaiter().GetResult());
    Eq("PUT", m3, "create method");
    Contains(body3!, "\"indices\":\"a,b\"", "多索引原样传给 ES");
    Contains(body3!, "\"include_global_state\":true", "勾选后包含全局状态");

    var (m4, uri4, _) = Capture(c => c.DeleteSnapshotAsync("backup", "snap-1").GetAwaiter().GetResult());
    Eq("DELETE", m4, "delete method");
    True(uri4.EndsWith("/_snapshot/backup/snap-1"), $"delete path => {uri4}");

    var (m5, uri5, _) = Capture(c => c.RestoreSnapshotAsync("backup", "snap-1", "a").GetAwaiter().GetResult());
    Eq("POST", m5, "restore method");
    True(uri5.Contains("/_snapshot/backup/snap-1/_restore"), $"restore path => {uri5}");
    Contains(uri5, "wait_for_completion=false", "恢复同样不阻塞");

    var (m6, uri6, _) = Capture(c => c.GetSnapshotStatusAsync().GetAwaiter().GetResult());
    Eq("GET", m6, "status method");
    True(uri6.EndsWith("/_snapshot/_status"), $"status path => {uri6}");

    foreach (var u in new[] { uri1, uri2, uri3, uri4, uri5, uri6 }) NoDoubleSlash(u, "snapshot");
});

Test("快照: 仓库名/快照名做 URL 转义（路径注入回归）", () =>
{
    var (_, uri1, _) = Capture(c => c.CreateSnapshotAsync("a/b", "s p").GetAwaiter().GetResult());
    False(uri1.Contains("/a/b/"), $"仓库名中的斜杠必须转义 => {uri1}");
    Contains(uri1, "a%2Fb", "仓库名已转义");
    Contains(uri1, "s%20p", "快照名已转义");

    var (_, uri2, _) = Capture(c => c.DeleteSnapshotRepositoryAsync("a/b").GetAwaiter().GetResult());
    Contains(uri2, "a%2Fb", "删除仓库同样转义");
});

Test("解析: 快照仓库（type + settings.location）", () =>
{
    const string json = """
        {
          "repo-b": { "type": "s3", "settings": { "bucket": "my-bucket" } },
          "repo-a": { "type": "fs", "settings": { "location": "/mnt/backups", "compress": true } }
        }
        """;
    var repos = EsParsers.ParseSnapshotRepositories(json);
    Eq(2, repos.Count, "repo count");
    Eq("repo-a", repos[0].Name, "按名称排序");
    Eq("fs", repos[0].Type, "type");
    Eq("/mnt/backups", repos[0].Location, "location");
    Contains(repos[0].SettingsJson, "compress", "settings 完整保留（详情展示用）");
    Eq("", repos[1].Location, "s3 仓库没有 location");
    Eq("s3", repos[1].Summary, "没有 location 时摘要回退为类型");
    Eq(0, EsParsers.ParseSnapshotRepositories("{}").Count, "空对象");
});

Test("解析: 快照列表（state/索引/耗时/分片/失败原因，按开始时间倒序）", () =>
{
    const string json = """
        {
          "snapshots": [
            { "snapshot": "old", "state": "FAILED", "indices": ["c"],
              "start_time_in_millis": 1600000000000, "duration_in_millis": 500,
              "failures": [ { "index": "c", "reason": "disk full" } ] },
            { "snapshot": "new", "state": "SUCCESS", "indices": ["a", "b"],
              "start_time_in_millis": 1700000000000, "duration_in_millis": 1500,
              "version": "7.15.2", "shards": { "total": 2, "successful": 2, "failed": 0 } }
          ]
        }
        """;
    var list = EsParsers.ParseSnapshots(json);
    Eq(2, list.Count, "snapshot count");
    Eq("new", list[0].Name, "按开始时间倒序（新的在前）");

    var newest = list[0];
    Eq("SUCCESS", newest.State, "state");
    Eq(2, newest.IndexCount, "index count");
    Eq("a, b", newest.Indices, "indices 逗号拼接");
    Eq("1.5 s", newest.Duration, "耗时人性化");
    Eq("2/2", newest.ShardsText, "分片摘要");
    Eq("7.15.2", newest.Version, "版本");
    True(newest.IsSuccess, "IsSuccess");
    True(!string.IsNullOrEmpty(newest.StartedAt), "开始时间已格式化");
    Eq("", newest.Failures, "成功快照没有失败原因");

    var oldest = list[1];
    True(!oldest.IsSuccess, "FAILED 不是成功");
    Contains(oldest.Failures, "disk full", "失败原因保留");
    Contains(oldest.Failures, "c", "失败原因带索引名");
    Eq(0, EsParsers.ParseSnapshots("{}").Count, "缺 snapshots 字段返回空");
    Eq(0, EsParsers.ParseSnapshots("""{"snapshots":[]}""").Count, "空数组");
});

Test("解析: 快照 start_time 为 0 / 非法值时不抛异常", () =>
{
    const string json = """
        { "snapshots": [ { "snapshot": "s", "state": "IN_PROGRESS",
                           "start_time_in_millis": 0, "duration_in_millis": 0 } ] }
        """;
    var list = EsParsers.ParseSnapshots(json);
    Eq(1, list.Count, "仍解析出条目");
    Eq("", list[0].StartedAt, "0 时间戳 → 空字符串而不是 1970 年");
    Eq("", list[0].Duration, "0 耗时 → 空字符串");
    True(list[0].IsPending, "IN_PROGRESS 判定为进行中");
    Eq(0, list[0].IndexCount, "缺 indices 字段 → 0");
});

// ============================================================
// 第 4 轮：搜索页索引下拉 + 五个列表（仓库/快照/恢复/SLM/ILM）
// ============================================================

Test("解析: 索引名列表（搜索页索引下拉的数据源）", () =>
{
    var names = EsParsers.ParseIndexNames("""[{"index":"logs-0002"},{"index":"logs-0001"},{"other":"x"}]""");
    Eq(2, names.Count, "只取带 index 字段的行");
    Eq("logs-0002", names[0], "保持响应顺序（排序由调用方决定）");
    Eq("logs-0001", names[1], "第二项");

    Eq(0, EsParsers.ParseIndexNames("[]").Count, "空数组");
    Eq(0, EsParsers.ParseIndexNames("""{"error":"no such index"}""").Count,
        "错误响应（对象）返回空而不是抛异常——失败原因由调用方展示");
});

Test("快照: SLM / ILM / 恢复进度 端点契约", () =>
{
    var (m1, uri1, _) = Capture(c => c.GetSlmPoliciesAsync().GetAwaiter().GetResult());
    Eq("GET", m1, "slm list method");
    True(uri1.EndsWith("/_slm/policy"), $"slm list path => {uri1}");

    var (m2, uri2, body2) = Capture(c => c.CreateSlmPolicyAsync(
        "daily/snap", """{"schedule":"0 30 1 * * ?","repository":"backup"}""").GetAwaiter().GetResult());
    Eq("PUT", m2, "slm create method");
    Contains(uri2, "/_slm/policy/daily%2Fsnap", "策略 ID 已转义");
    Contains(body2!, "schedule", "body 原样透传");

    var (m3, uri3, _) = Capture(c => c.ExecuteSlmPolicyAsync("p1").GetAwaiter().GetResult());
    Eq("POST", m3, "slm execute method");
    True(uri3.EndsWith("/_slm/policy/p1/_execute"), $"slm execute path => {uri3}");

    var (m4, uri4, _) = Capture(c => c.DeleteSlmPolicyAsync("p1").GetAwaiter().GetResult());
    Eq("DELETE", m4, "slm delete method");
    True(uri4.EndsWith("/_slm/policy/p1"), $"slm delete path => {uri4}");

    var (m5, uri5, _) = Capture(c => c.GetIlmPoliciesAsync().GetAwaiter().GetResult());
    Eq("GET", m5, "ilm list method");
    True(uri5.EndsWith("/_ilm/policy"), $"ilm list path => {uri5}");

    var (m6, uri6, _) = Capture(c => c.CreateIlmPolicyAsync("p 1", """{"policy":{"phases":{}}}""").GetAwaiter().GetResult());
    Eq("PUT", m6, "ilm create method");
    Contains(uri6, "/_ilm/policy/p%201", "策略 ID 已转义");

    var (m7, uri7, _) = Capture(c => c.DeleteIlmPolicyAsync("p1").GetAwaiter().GetResult());
    Eq("DELETE", m7, "ilm delete method");
    True(uri7.EndsWith("/_ilm/policy/p1"), $"ilm delete path => {uri7}");

    var (m8, uri8, _) = Capture(c => c.GetRecoveryStatusAsync().GetAwaiter().GetResult());
    Eq("GET", m8, "recovery method");
    Contains(uri8, "/_recovery?", "recovery path");
    Contains(uri8, "active_only=true", "只看进行中的恢复");

    var (m9, uri9, _) = Capture(c => c.GetSnapshotDetailAsync("backup", "snap-1").GetAwaiter().GetResult());
    Eq("GET", m9, "snapshot detail method");
    True(uri9.EndsWith("/_snapshot/backup/snap-1"), $"detail path => {uri9}");

    foreach (var u in new[] { uri1, uri2, uri3, uri4, uri5, uri6, uri7, uri8, uri9 })
        NoDoubleSlash(u, "snapshot extra");
});

Test("快照: 恢复支持重命名（避免覆盖线上同名索引）", () =>
{
    var (_, _, body) = Capture(c => c.RestoreSnapshotAsync(
        "backup", "snap-1", "a", false, "index_(.+)", "restored_$1").GetAwaiter().GetResult());

    // 注意：JsonObject.ToJsonString() 用的是默认编码器，会把 "+" 写成 "\u002B"、
    // "<" 写成 "\u003C"。这在 JSON 里是等价的转义，ES 解析后拿到的是原字符，
    // 所以这里按"反序列化后的值"断言，而不是按字面量比对原始报文。
    using (var doc = JsonDocument.Parse(body!))
    {
        var root = doc.RootElement;
        Eq("index_(.+)", root.GetProperty("rename_pattern").GetString()!, "重命名正则原样送达");
        Eq("restored_$1", root.GetProperty("rename_replacement").GetString()!, "替换串原样送达");
    }

    var (_, _, plain) = Capture(c => c.RestoreSnapshotAsync("backup", "snap-1").GetAwaiter().GetResult());
    False(plain!.Contains("rename_pattern"),
        "没填就不要带字段：空字符串会被 ES 当成非法正则，恢复整单失败");
    Contains(plain, "\"ignore_unavailable\":true", "默认忽略不存在的索引");
    Contains(plain, "\"indices\":\"*\"", "未指定索引时视为全部");
});

Test("解析: SLM 策略（调度/仓库/保留/时间，兼容 7.x 对象与 8.x 字符串）", () =>
{
    const string json = """
        {
          "daily": {
            "policy": {
              "name": "<daily-{now/d}>", "schedule": "0 30 1 * * ?", "repository": "backup",
              "config": { "indices": ["logs-*", "metrics-*"], "include_global_state": false },
              "retention": { "expire_after": "30d", "min_count": 5, "max_count": 50 }
            },
            "next_execution_millis": 1700000000000,
            "last_success": { "snapshot_name": "daily-2024.01.01", "time": 1699999999000 },
            "last_failure": null,
            "stats": { "snapshots_taken": 12, "snapshots_failed": 1, "snapshots_deleted": 2 }
          },
          "hourly": {
            "policy": { "schedule": "0 0 * * * ?", "repository": "backup2" },
            "next_execution": "2024-01-02T03:04:05.000Z",
            "last_success": "2024-01-01T00:00:00.000Z"
          },
          "isoTime": {
            "policy": { "schedule": "0 0 * * * ?", "repository": "backup3" },
            "last_failure": { "snapshot_name": "isoTime-1", "time_string": "2024-01-01T00:00:00.000Z",
                              "reason": "boom" }
          }
        }
        """;
    var list = EsParsers.ParseSlmPolicies(json);
    Eq(3, list.Count, "policy count");
    Eq("daily", list[0].PolicyId, "按策略 ID 排序");

    var daily = list[0];
    Eq("<daily-{now/d}>", daily.SnapshotNameTemplate, "快照名模板");
    Eq("0 30 1 * * ?", daily.Schedule, "调度");
    Eq("backup", daily.Repository, "仓库");
    Eq("logs-*, metrics-*", daily.Indices, "索引列表拼接");
    Contains(daily.RetentionText, "30d", "过期时间");
    Contains(daily.RetentionText, "5", "最少保留数");
    Contains(daily.RetentionText, "50", "最多保留数");
    True(!string.IsNullOrEmpty(daily.NextExecution), "下次执行时间已格式化");
    Contains(daily.LastSuccess, "daily-2024.01.01", "最近成功的快照名");
    Eq("", daily.LastFailure, "last_failure 为 null → 空串");
    False(daily.HasFailure, "没有失败");
    Contains(daily.StatsText, "12", "统计：已执行次数");
    Contains(daily.StatsText, "1", "统计：失败次数");
    Contains(daily.PolicyJson, "schedule", "策略原文保留（详情展示用）");

    var hourly = list[1];
    True(!string.IsNullOrEmpty(hourly.NextExecution), "8.x 的 ISO 字符串形态也要能显示");
    False(hourly.NextExecution.Contains('T'), "ISO 时间已转成本地时间格式");
    True(!string.IsNullOrEmpty(hourly.LastSuccess), "字符串形态的 last_success");
    Eq("", hourly.RetentionText, "没有 retention → 空串（不是 null）");
    Eq("", hourly.Indices, "没有 config → 空串");

    // last_success/last_failure 对象里的伴随字段叫 time_string（不是 time_millis）：
    // 缺 time 毫秒时必须回退到它，否则这一列会显示空白。
    // 注意这里必须**正向**断言格式化后的值：只写 False(Contains("…T00:00:00")) 的话，
    // 时间整个缺失时也会通过，等于没测（这个坑第一次就是这样踩到的）。
    var isoTime = list[2];
    Contains(isoTime.LastFailure, "isoTime-1", "失败快照名");
    Contains(isoTime.LastFailure, "boom", "失败原因");
    string expectedWhen = DateTimeOffset.Parse("2024-01-01T00:00:00.000Z",
            System.Globalization.CultureInfo.InvariantCulture)
        .ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
    Contains(isoTime.LastFailure, expectedWhen, "time_string 必须被解析成本地时间并显示（伴随字段是 time_string）");
    False(isoTime.LastFailure.Contains("T00:00:00"), "不得原样显示 ISO 串");
    True(isoTime.HasFailure, "有 last_failure → HasFailure");
    Eq("", isoTime.NextExecution, "没有 next_execution* → 空串（不是 null）");
    Eq(0, EsParsers.ParseSlmPolicies("{}").Count, "空对象");
});

Test("解析: ILM 生命周期策略（阶段链顺序/使用中索引/修改时间）", () =>
{
    // fixture 用**真实响应形状**（照 ES 7.17 / 8.17 / main 的 LifecyclePolicyMetadata.toXContent，
    // 三个版本逐字核对过）：
    //   modified_date        是 epoch 毫秒（declareLong）
    //   modified_date_string 才是 ISO 串
    // 早先这里写成 "modified_date": "…ISO…" 的假形状，于是"把毫秒当 ISO 解析"的缺陷在
    // True(!IsNullOrEmpty(...)) 这种断言下 102/102 全绿地存在 —— 新断言必须能抓出它。
    // phases 的书写顺序也刻意打乱：ES 那边 phases 是 HashMap，返回顺序既不是生命周期顺序、也不稳定。
    const string json = """
        {
          "logs": {
            "version": 3,
            "modified_date": 1718452800000,
            "modified_date_string": "2024-06-15T12:00:00.000Z",
            "policy": { "phases": {
              "delete": { "min_age": "30d", "actions": { "delete": {} } },
              "hot": { "actions": {} },
              "warm": { "actions": {} } } },
            "in_use_by": { "indices": ["logs-0001", "logs-0002"], "data_streams": [], "composable_templates": [] }
          },
          "full": {
            "version": 1,
            "modified_date_string": "2024-06-15T12:00:00.000Z",
            "policy": { "phases": {
              "frozen": { "actions": {} }, "delete": { "actions": {} }, "cold": { "actions": {} },
              "hot": { "actions": {} }, "warm": { "actions": {} }, "archive": { "actions": {} } } }
          },
          "empty": { "version": 1, "policy": { "phases": {} } }
        }
        """;
    var list = EsParsers.ParseIlmPolicies(json);
    Eq(3, list.Count, "policy count");
    Eq("empty", list[0].PolicyId, "按策略 ID 排序");
    Eq("full", list[1].PolicyId, "按策略 ID 排序");
    Eq("logs", list[2].PolicyId, "按策略 ID 排序");

    var logs = list[2];
    // 阶段链必须按 ILM 执行顺序，而不是 ES 的返回顺序（fixture 里 delete 排在最前）
    Eq("hot → warm → delete", logs.PhasesText, "阶段链按 hot/warm/cold/frozen/delete 排序");
    // 未知阶段排在已知阶段之后，且仍保持 ES 的返回相对顺序
    Eq("hot → warm → cold → frozen → delete → archive", list[1].PhasesText, "五个标准阶段 + 未知阶段殿后");
    Eq(2, logs.IndicesInUseCount, "使用中索引数");
    Eq("logs-0001, logs-0002", logs.IndicesInUse, "使用中索引拼接");

    // 修改时间：既要"是本地时间格式"，也要"等于 modified_date 毫秒对应的那个瞬间"。
    // 只断言非空抓不到"原样显示裸毫秒"，所以这里逐项钉死。
    True(System.Text.RegularExpressions.Regex.IsMatch(logs.ModifiedDate, @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$"),
        $"修改时间应是 yyyy-MM-dd HH:mm:ss，实际 <{logs.ModifiedDate}>（裸毫秒 = 毫秒字段被当成 ISO 解析了）");
    False(logs.ModifiedDate.Contains("1718452800000"), "不得显示原始 epoch 毫秒");
    var when = DateTime.ParseExact(logs.ModifiedDate, "yyyy-MM-dd HH:mm:ss",
        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal);
    Eq(DateTimeOffset.FromUnixTimeMilliseconds(1718452800000).UtcDateTime, when.ToUniversalTime(),
        "修改时间应对应 modified_date（毫秒）那一刻");
    // 只有 ISO 伴随字段、没有毫秒时也要能显示
    True(System.Text.RegularExpressions.Regex.IsMatch(list[1].ModifiedDate, @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$"),
        $"缺 modified_date 毫秒时应回退 modified_date_string，实际 <{list[1].ModifiedDate}>");
    Contains(logs.PolicyJson, "phases", "策略原文保留");

    Eq("", list[0].PhasesText, "空 phases → 空阶段链");
    Eq("", list[0].ModifiedDate, "没有修改时间 → 空串（不是 null）");
    Eq(0, list[0].IndicesInUseCount, "没有 in_use_by → 0");
    Eq(0, EsParsers.ParseIlmPolicies("{}").Count, "空对象");
});

Test("解析: 恢复进度（分片级别，DONE 判定与进度显示）", () =>
{
    const string json = """
        {
          "restored-1": {
            "shards": [
              { "id": 0, "type": "SNAPSHOT", "stage": "DONE", "total_time_in_millis": 1500,
                "source": { "host": "10.0.0.1", "name": "node-1" },
                "target": { "host": "10.0.0.2", "name": "node-2" },
                "index": { "size": { "total_in_bytes": 2097152, "recovered_in_bytes": 2097152 },
                           "files": { "total": 4, "recovered": 4, "percent": "100.0%" } } },
              { "id": 1, "type": "SNAPSHOT", "stage": "INDEX", "total_time_in_millis": 0,
                "index": { "files": { "recovered": 2, "percent": "42.5%" } } },
              { "id": 2, "type": "SNAPSHOT", "stage": "DONE",
                "source": { "repository": "repo-a", "snapshot": "snap-1" } }
            ]
          }
        }
        """;
    var rows = EsParsers.ParseRecovery(json);
    Eq(3, rows.Count, "每个分片一行");
    Eq("restored-1", rows[0].Index, "索引名");
    Eq("0", rows[0].Shard, "分片号");
    Eq("SNAPSHOT", rows[0].Type, "恢复类型");
    Eq("DONE", rows[0].Stage, "阶段");
    Eq("100.0%", rows[0].FilesPercent, "文件百分比");
    Eq("10.0.0.1", rows[0].Source, "来源节点");
    Eq("10.0.0.2", rows[0].Target, "目标节点");
    True(!string.IsNullOrEmpty(rows[0].BytesText), "字节进度");
    True(!string.IsNullOrEmpty(rows[0].TimeText), "耗时");
    True(rows[0].IsDone, "DONE 判定完成");

    False(rows[1].IsDone, "INDEX 阶段未完成");
    Eq("42.5%", rows[1].FilesPercent, "进行中的百分比");
    Eq("", rows[1].BytesText, "缺 size → 空串而不是抛异常");
    Eq("", rows[1].Source, "缺 source → 空串");
    Eq("repo-a/snap-1", rows[2].Source, "快照恢复的 source 没有 host/name → 回退成 repository/snapshot");
    Eq(0, EsParsers.ParseRecovery("{}").Count, "空对象");
    Eq(0, EsParsers.ParseRecovery("""{"idx":{"no_shards":true}}""").Count, "没有 shards 字段");
});

// ============================================================
// 图标：矢量路径语法与视图框校验（Linux 可跑）
//
// 为什么放在这里：图标数据在 Core（纯字符串），WPF 侧用 Geometry.Parse 渲染。
// 路径数据写坏时 Geometry.Parse 会在**真机运行期**抛 FormatException，而本机跑不了 WPF。
// 于是用这个独立的迷你解析器逐条校验语法 / 命令元数 / 坐标范围，
// 把"运行期才炸"变成"构建期就红"。
// ============================================================

// 路径迷你语言 → (命令, 参数) 序列。只接受绝对命令，与 AppIcons 的约定一致。
List<(char Cmd, double[] Args)> ParseIconPath(string data)
{
    var arity = new Dictionary<char, int>
        { ['M'] = 2, ['L'] = 2, ['H'] = 1, ['V'] = 1, ['C'] = 6, ['A'] = 7, ['Z'] = 0 };
    var result = new List<(char, double[])>();
    int i = 0;
    while (i < data.Length)
    {
        char c = data[i];
        if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
        if (!char.IsLetter(c))
            throw new Exception($"意外的字符 '{c}'：命令必须以字母开头（不允许省略命令的隐式重复）");
        if (!arity.TryGetValue(c, out int need))
            throw new Exception($"不支持的路径命令 '{c}'（只允许 M/L/H/V/C/A/Z）");
        if (char.IsLower(c))
            throw new Exception($"不允许相对命令 '{c}'（相对命令在图标里易产生歧义）");
        i++;
        if (c == 'Z') { result.Add((c, Array.Empty<double>())); continue; }

        var args = new List<double>();
        while (args.Count < need)
        {
            while (i < data.Length && (char.IsWhiteSpace(data[i]) || data[i] == ',')) i++;
            int start = i;
            while (i < data.Length && (char.IsDigit(data[i]) || data[i] is '.' or '-' or '+' or 'e' or 'E')) i++;
            if (i == start)
                throw new Exception($"命令 '{c}' 参数不足：需要 {need} 个，实际只有 {args.Count} 个");
            if (!double.TryParse(data[start..i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
                throw new Exception($"命令 '{c}' 的参数不是合法数字：'{data[start..i]}'");
            args.Add(v);
        }
        result.Add((c, args.ToArray()));
    }
    return result;
}

void InIconRange(double v, string context)
{
    if (v < -0.01 || v > 24.01)
        throw new Exception($"{context}: 坐标 {v} 超出 0..24 视图框（会导致图标大小不一致或溢出）");
}

// 逐个子路径校验"纯弧线闭合形状"的弧数必须成对。
// 这是"想画整圆但只写了一段弧"的典型错误：一个 A + Z 只会画出一条弦（D 形），不是圆。
// 开放子路径（如 Refresh 的 3/4 圆箭头）允许奇数段弧 —— 一刀切会让规则变成误报源。
void ValidateArcSubpaths(string name, List<(char Cmd, double[] Args)> cmds)
{
    int arcs = 0;
    bool closed = false, hasLine = false, inSub = false;

    void Flush()
    {
        if (inSub && closed && !hasLine && arcs > 0 && arcs % 2 != 0)
            throw new Exception($"{name}: 纯弧线闭合子路径的弧数为奇数（{arcs}）——整圆必须用两段半圆弧");
        arcs = 0;
        closed = false;
        hasLine = false;
    }

    foreach (var (c, _) in cmds)
    {
        switch (c)
        {
            case 'M': Flush(); inSub = true; break;
            case 'A': arcs++; break;
            case 'L':
            case 'H':
            case 'V':
            case 'C': hasLine = true; break;
            case 'Z': closed = true; break;
        }
    }
    Flush();
}

Test("图标: 每条路径语法合法、命令元数正确、坐标落在 0..24 视图框", () =>
{
    True(AppIcons.All.Count >= 20, $"图标数量过少（{AppIcons.All.Count}），疑似漏登记");
    var names = new HashSet<string>();
    foreach (var (name, data) in AppIcons.All)
    {
        True(names.Add(name), $"{name}: 名称重复");
        var cmds = ParseIconPath(data);
        True(cmds.Count > 0, $"{name}: 没有任何命令");
        Eq('M', cmds[0].Cmd, $"{name}: 路径必须以 M 开头");

        foreach (var (c, a) in cmds)
        {
            switch (c)
            {
                case 'M':
                case 'L':
                    InIconRange(a[0], $"{name}.{c}.x");
                    InIconRange(a[1], $"{name}.{c}.y");
                    break;
                case 'H':
                    InIconRange(a[0], $"{name}.H");
                    break;
                case 'V':
                    InIconRange(a[0], $"{name}.V");
                    break;
                case 'C':
                    for (int k = 0; k < 6; k++) InIconRange(a[k], $"{name}.C[{k}]");
                    break;
                case 'A':
                    True(a[0] > 0 && a[1] > 0, $"{name}: 圆弧半径必须为正");
                    True(a[3] is 0 or 1, $"{name}: large-arc-flag 只能是 0 或 1");
                    True(a[4] is 0 or 1, $"{name}: sweep-flag 只能是 0 或 1");
                    InIconRange(a[5], $"{name}.A.x");
                    InIconRange(a[6], $"{name}.A.y");
                    break;
            }
        }
        // 整圆写法必须是"两段半圆弧"，否则会渲染成缺口圆 / D 形
        ValidateArcSubpaths(name, cmds);
    }
});

Test("图标: 弧线子路径校验本身有效（能抓出单弧假圆，且不误报开放弧）", () =>
{
    // 必须失败：一个 A + Z = 一条弦，不是圆
    try
    {
        ValidateArcSubpaths("bad", ParseIconPath("M4 12 A8 8 0 1 0 20 12 Z"));
        throw new Exception("未能抓出单弧闭合假圆（规则已失效）");
    }
    catch (Exception ex) when (ex.Message.Contains("弧数为奇数"))
    {
        // 期望路径
    }

    // 必须通过：两段半圆弧 = 整圆
    ValidateArcSubpaths("ok", ParseIconPath("M4 12 A8 8 0 1 0 20 12 A8 8 0 1 0 4 12 Z"));
    // 必须通过：开放弧（3/4 圆箭头，Refresh 就是这种）
    ValidateArcSubpaths("open", ParseIconPath("M19.6 12 A7.6 7.6 0 1 1 16.7 6.1 M16.7 6.1 H20.2"));
    // 必须通过：直线 + 单弧闭合（合法的 D 形）
    ValidateArcSubpaths("dshape", ParseIconPath("M4 4 V20 A8 8 0 0 1 4 4 Z"));
});

Test("图标: 每个 public const 图标都已登记进 All（防止新增图标漏登记）", () =>
{
    var consts = typeof(AppIcons)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => f.Name)
        .ToHashSet();
    var registered = AppIcons.All.Select(x => x.Name).ToHashSet();
    var missing = consts.Except(registered).OrderBy(x => x).ToList();
    Eq(0, missing.Count, $"未登记进 AppIcons.All 的图标：{string.Join(", ", missing)}");
});

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