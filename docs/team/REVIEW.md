# REVIEW.md — 双轴审查报告（标准轴 + 规格轴）

- 审查对象：`elastic-desktop-manager-wpf`（JavaFX → WPF/.NET 8 移植）
- 审查方式：独立子代理，静态审查（EsClient/模型/服务/i18n/Json + 全部 ViewModel + 关键 XAML/代码后置）+ Core 单测执行 + 与源 JavaFX 项目（`elastic-desktop-manager-master`）逐功能对照
- 环境：Linux（WPF 无法运行 → 以 `EnableWindowsTargeting` 编译验证 + Core 单测为证据；「无法运行 WPF」不视为 blocker，涉及运行时行为处已标注证据类别）
- 构建验证（审查时）：`NUGET_HTTP_CACHE_PATH=... NUGET_PACKAGES=... dotnet build ElasticDesktopManager.sln -v q` → **Build succeeded, 0 Warning(s), 0 Error(s)**
- 单测验证（审查时）：`dotnet run --project tests/ElasticDesktopManager.Tests -c Release` → **27/27 通过，0 失败**
- 时序说明：审查期间父团队仍在并行修改（10:32–10:44 Token 有 `ConnectionsViewModel.cs`、`ConnectionFormDialog.xaml.cs`、`context.md` 等变更；解决方案文件由 `.slnx` 更名为 `.sln`）。报告全部发现均基于**审查结束时**的文件内容复核，行号以此为准。

---

## 1. 总评

这是一个工程质量远超平均水准的移植：分层干净（Core 无 UI 依赖、Linux 可测），EsClient 对 HttpClient/HttpClientHandler 生命周期、SSL 回调条件安装、Basic 认证、超时与用户取消区分、错误翻译的处理均正确，27 个单测真实覆盖了 AC-8 列出的全部核心逻辑，UI 层所有 `ObservableCollection` 替换（包括易踩坑的 `ReplaceAll`）经逐一核查都发生在 UI 线程，XAML 绑定绝大多数正确，i18n 双词典覆盖 200+ key。**标准轴没有发现任何崩溃 / 数据损坏 / SSL 失效类问题**；唯一达到阻断级（P0）的缺陷是「按查询更新」在功能上是一个**静默空操作**（发送的 `_update_by_query` 请求体只有 `query` 没有 `script`，匹配文档不会被修改，UI 却确认并提示“操作成功”），这使 AC-6 的 “update by query” 交付物只有形式没有实质。其余为一批 P2 级改进建议（i18n 一致性、SQL cursor 关闭、CSV 导出范围、REST GET 携带 body 被丢弃、存储健壮性等）。

---

## 2. 标准轴发现

### P0（必须修复）

**P0-1 搜索页「按查询更新」是静默空操作（no-op），与 AC-6 不符**
- 位置：`src/ElasticDesktopManager/ViewModels/SearchViewModel.cs:298-317`（关键行为 309-313 行），源头 `Core/Es/EsClient.cs:129-130`（`UpdateByQueryAsync` 仅透传 body）
- 理由：`ModifyByQueryAsync(isUpdate: true)` 发送的 body 是 `BuildDsl(withOptions:false)`，即只有 `{"query": {...}}`。ES 的 `_update_by_query` 在没有 `script`（或 doc 替换载荷）时对匹配文档**不做任何修改**（响应 `updated: 0`）。而 UI 流程是：确认框「确定按当前条件更新匹配文档？」→ 发送 → Toast「操作成功」→ 重跑搜索。用户会以为文档已更新，实际什么都没发生——比报错更危险的是**静默的假成功**。
- 对照：源项目 `elastic-desktop-manager-master/src/main/java/com/lxwise/elastic/gui/ClusterSearchController.java` `updateByQueryAction`（约 880-925 行）会弹出字段/值更新表，生成 `script.inline`（`ctx._source['f']=v;`）并随 `query` 一起 POST。移植时去掉了 script 编辑 UI，但没有提供任何替代的 script 输入手段，导致该功能退化为 no-op。
- 修复方向（任选，至少其一）：① 提供 script 输入（如 "查看 DSL" 弹窗允许编辑完整 body，并把提交体原样交给 `_update_by_query`）；② 在 DSL 查看器中增删 `script` 段；③ 若本期不打算实现真实更新，应在确认框/按钮上明确降级或禁用以避免虚假成功。**不得**在无 script 的情况下提示「操作成功」。

### P1（应修复）

未再发现其他 P1。经逐文件核查，以下高风险项均**不成立**（记录排除结论，供后续审查复用）：

- **ReplaceAll 跨线程**：`ObservableList.ReplaceAll`（`Mvvm/ObservableObject.cs:98-105`）直接操作 `Items` 并广播 Reset，全部调用点（ConnectionsViewModel.Reload、SqlViewModel.RenderFromCurrent、SearchViewModel.BuildTable、IndicesViewModel.ApplyPage、HealthViewModel.ApplyHealth、Nodes/ShardsViewModel、RestHistoryViewModel.Reload）经追查都运行在 UI 线程（await 后续延续在 Dispatcher SynchronizationContext 上），**无跨线程风险**。注意：`MainViewModel.OnConnectionChanged`（MainViewModel.cs:138-151）的 `ReloadAsync` 亦在 UI 线程触发。
- **HttpClient/Handler 生命周期**：每连接一个 EsClient（ADR-1），切换/断开经 `EsSession`（EsSession.cs:35-40, 61-69）Dispose；`Dispose`（EsClient.cs:244-249）幂等且有 `_disposed` 守卫；`ExecuteAsync` 入口有 `ObjectDisposedException.ThrowIf`（160 行）。
- **SSL 回调条件安装**：`CreateHandler`（EsClient.cs:48-64）仅在 `SkipSslVerify=true` 时安装 `DangerousAcceptAnyServerCertificateValidator`，`false`/默认保持 NULL；单测 3 例覆盖正反决策（Program.cs:63-77）。
- **超时与用户取消区分**：`CreateLinkedTokenSource + CancelAfter`（EsClient.cs:167-168）+ `catch (OperationCanceledException) when (!ct.IsCancellationRequested)`（189-191 行）将超时翻译为 `EsException("请求超时")`，用户取消原样上抛。单测覆盖（Program.cs:155-169）。

### P2（建议）

1. **REST 历史空态绑定到不存在的属性**：`Views/RestHistoryWindow.xaml:30` `Visibility="{Binding HasItems, ...}"`，而 `RestHistoryViewModel`（RestHistoryViewModel.cs）无 `HasItems` 属性 → 运行期绑定失败（输出窗口报 binding error）。当前靠代码后置（RestHistoryWindow.xaml.cs:30-31）构造期赋值兜底，但**删除最后一条后空态提示不会再出现**（网格空着但提示已 Collapsed）。建议在 VM 增加 `HasItems => Items.Count > 0` 并在 `Reload()` 后补发通知。
2. **i18n 硬编码中文**：`ConnectionFormDialog.xaml.cs:110-111`、`FolderFormDialog.xaml.cs:33`、`SqlViewModel.cs:96`、`RestViewModel.cs:94`、`SearchViewModel.cs:237` 均用 `L(key) + " 不能为空"` 拼串，切到 en 后显示「…不能为空」中英混杂。应提供 `*.required` 类完整 key。
3. **i18n 漏词**：`common.delete` 在 `RestHistoryWindow.xaml.cs:20` 使用但 zh/en 词典均未定义（Localization.cs 无此 key）→ UI 显示原始 key。其余 132 个使用点全部有定义，双词典 201 个 key 对齐良好。另：`MainWindow.xaml:43`「⚠ 跳过 SSL 验证」、`IndicesView.xaml:23` `StringFormat=Total: {0}`、`HealthView.xaml` 中 "Node/Cluster UUID/Version/Elasticsearch"、`IndicesView.xaml`「‹ 上一页/下一页 ›」等为写死英文/中文，未走词典（P2 级观感问题）。
4. **SQL cursor 从不关闭**：`EsClient.CloseSqlAsync`（EsClient.cs:145-149）实现正确但全代码库无调用；SqlViewModel 翻页/离开页面均不 `/_sql/close`。源项目在刷新/退出页面时显式关闭（`ClusterSqlController.java:611,712`）。后果：ES 服务端 cursor 需等 keep_alive 过期才释放（短时占用，非严重）。建议在完成/离开时关闭。
5. **SQL CSV 仅导出当前页 + UTF-8 无 BOM**：`SqlViewModel.cs:176-205` 从 `Rows`（= 当前页）导出，而 Java 端是跨页累加全部数据后导出；且 `WriteAllText(..., Encoding.UTF8)` 无 BOM，Excel 打开中文会乱码。建议导出全部分页结果（或明确标注当前页），并写 BOM（`new UTF8Encoding(true)`）。
6. **REST 控制台 GET 携带 body 被静默丢弃**：`EsClient.cs:178-182` 只为 POST/PUT/PATCH/DELETE 挂 Content；用户填 GET + body（如 `GET /_search {查询体}`）时 body 被忽略且无任何提示。Java 端 `executeRest` 对任意方法都 `setJsonEntity`。建议至少对丢弃行为给出提示，或与 Java 对齐。
7. **存储健壮性**：
   - `ConfigService.Load`（ConfigService.cs:28-32）吞掉解析异常返回空表 → 用户下次保存（Upsert → Save，47 行）会把坏文件覆盖为空列表，**连接配置静默丢失**。建议损坏时保留备份（如改名 `.corrupt`）并提示。
   - 三个服务均为 `File.WriteAllText` 非原子写，中途崩溃可能留下截断文件；`SettingService`/`CommandHistoryService` 同样吞异常回退。（建议原子写：临时文件 + `File.Replace`。）
8. **时间窗口外的超时翻译缺口**：`EsClient.cs:200` `ReadAsStringAsync(cts.Token)` 位于 try 之外；理论上若超时落在响应体读取阶段，抛的是裸 `TaskCanceledException` 而非 `EsException`（因 `ResponseContentRead` 实际已缓冲，概率极低）。建议把读正文也纳入翻译 catch。
9. **显示层方案冗余**：`ConfigProperty.DisplayName`（ConfigProperty.cs:48）与 `MainViewModel.ConnectionLabel`（MainViewModel.cs:146）在 `Servers` 已含协议前缀（如 `https://x:9200`）时会显示成 `http://https://x:9200`。建议统一显示逻辑：先剥离已含 scheme。
10. **SQL 超时未独立配置**：`SettingProperty.SqlTimeoutMs`（SettingProperty.cs:25）恒等于 `TimeoutMs`；Java 默认 SQL 超时 2 分钟（`ElasticManage.java` `SQL_TIMEOUT_MS`）。当前设置页只有一个超时，SQL 大查询与普通请求同限（60s 默认），可接受但建议说明或在设置页分开。
11. **启动打开连接对话框默认值不同**：`SettingProperty.OpenDialog`（SettingProperty.cs:17）默认 `true`，Java（`SettingProperty.java`）默认 `false`。AC-5 只要求“可选择”，不算违反，但行为与源项目相反，交付说明里应写明。
12. **健康页轮询不随页面可见性暂停**：`HealthViewModel.cs:43-48` 的 `DispatcherTimer` 每 30s 拉一次 `/_cluster/health` + `/`（页签隐藏在后台也继续，页面 VM 被 MainViewModel 永久缓存）。建议用页面 Loaded/Unloaded（或仅当前页可见时）暂停。
13. **删除当前已连接集群不断开会话**：`ConnectionsViewModel.Delete`（179-186 行）删除配置后不 `EsSession.Disconnect()`，若删除的是当前连接，会话仍持有旧客户端。建议联动断开。
14. **`AsyncRelayCommand` 的 `ICommand.Execute` 为 async void**（ObservableObject.cs:72）：异常经 SynchronizationContext 汇到 `DispatcherUnhandledException`（App.xaml.cs:21-25 弹 MessageBox），不会崩溃，但建议统一走 `ExecuteAsync` 路径减少意外。
15. **仓库卫生**：项目根残留一个名为 `$(MSBuildThisFileDirectory).packages` 的字面量目录（系未展开的 MSBuild 属性，`ls` 可见），且 `.gitignore` 的 `.packages/` 规则不会匹配它；项目目录尚无 git 仓库（交付/回溯缺口）。建议清理该目录并在交付前 `git init` + 首次提交。
16. **死 API 面**：`EsClient.DeleteDocumentByIdAsync/ListTemplatesAsync/DeleteTemplateAsync` 无 UI 调用（与 Java 端一致，属保留面）；`CloseSqlAsync` 见 P2-4。可保留，但建议注释标注“保留 API，暂无 UI”。

---

## 3. 规格轴：AC 清单逐条核对

| AC | 验收项 | 证据（代码/测试/构建） | 结论 |
|---|---|---|---|
| 1 | 三项目（Core net8.0 / WPF net8.0-windows / Tests net8.0），Linux 零错误构建 | `ElasticDesktopManager.sln` 含 Core+WPF+Tests；`Directory.Build.props` 设 `EnableWindowsTargeting`；实测 `dotnet build` **0 Warning 0 Error** | ✅ 满足 |
| 2 | http/https 基址；`SkipSslVerify=true` 安装回调、`false` 保持默认校验 | `ConfigProperty.BaseUrl()`（ConfigProperty.cs:51-67，含 scheme 规整/URL 优先）；`EsClient.CreateHandler`（EsClient.cs:48-64）条件安装；测试 6 例 URL + 3 例 SSL skip 决策（Program.cs:53-77）；链路：表单勾选（ConnectionFormDialog.xaml.cs:119）→ 落盘（ConfigService.Upsert）→ 树节点 → Connect（ConnectionsViewModel.cs:225）→ handler | ✅ 满足（SSL 主诉求完整闭环） |
| 3 | Basic 认证；超时可配置且生效 | `ExecuteAsync`（EsClient.cs:172-176）Security+Username 非空才加 Basic 头；超时来自设置（`App.Settings.Timeout`）→ `CancelAfter`，测试覆盖 auth 头/无 auth 头/超时翻译（Program.cs:98-169） | ✅ 满足 |
| 4 | REST 调用与源一致：health/indices/nodes/shards/_sql cursor/_search/update_by_query/delete_by_query/索引操作 | 逐项对照 `ElasticManage.java`：端点全部一致（EsClient.cs:66-149）；索引 refresh/flush/clear cache 的 WPF 版 `/{index}/_refresh` 等**修正了 Java 版的漏斜杠 bug**（Java `"/"+index+"_refresh"`）；SQL cursor 三接口齐全 | ✅ 满足（**例外见 P0-1**：update_by_query 虽发请求但 body 无 script，功能为空操作；SQL cursor 从不 close 见 P2-4） |
| 5 | 连接管理：文件夹+集群树、增删改查、测试、连接、过滤、启动对话框选项 | ConnectionView（树+过滤+启动选项）+ ConnectionsViewModel（CRUD/测试/连接/级联删除）+ 表单；`OpenDialog` 设置项生效（App.xaml.cs:34-37） | ✅ 满足（默认 true vs Java false，P2-11） |
| 6 | 视图齐全：健康/节点/分片/索引/ REST+历史/ SQL+CSV/搜索构建器+DSL+update-delete/设置/关于 | 全部 UserControl/Window 存在并在 MainViewModel 导航注册（MainViewModel.cs:184-193）；功能核对：健康（指标+ES 信息+30s 刷新）、节点（表格+过滤）、分片（表格+状态统计）、索引（分页+过滤+详情/统计/refresh/flush/clear/open/close+健康色点）、REST（方法/路径/body/格式化/历史 100 条）、SQL（表格/JSON/游标翻页/CSV）、搜索（bool×操作符构建器→DSL JSON→结果表→update/delete by query）、设置（语言/主题/超时/关闭行为）、关于；delete_by_query 正确可用 | ⚠️ **基本满足，一处分叉**：update by query 为静默 no-op（P0-1）；SQL CSV 仅导出当前页（P2-5）；搜索"显示字段选择器"按 ADR-9 降级为自动列（文档已声明） |
| 7 | JSON 文件存储；历史限 100；`EDM_DATA_DIR` 覆盖 | ConfigService/SettingService/CommandHistoryService（JSON）；`MaxCount=100`（CommandHistoryService.cs:9）且测试验证上限与倒序（Program.cs:277-287）；`AppPaths` 支持 `EDM_DATA_DIR`（AppPaths.cs:8-22） | ✅ 满足 |
| 8 | 单测覆盖：URL 规整/SSL skip/SQL body/配置服务增删改/索引解析/JSON 美化，Linux 通过 | 实测 **27/27 通过**（Release）：URL 6、SSL 3、HTTP 行为 6（路径拼接/Basic/无认证/SQL body/错误码翻译/超时）、解析 5、存储 3、Json+i18n 3 | ✅ 满足（无 mock 框架、无网络、可重复） |
| 9 | 双语言 i18n，设置页可切换 | Localization zh/en 双词典 201 key；`Localization.SetLanguage` 事件驱动导航刷新；设置页切换即时生效（SettingsViewModel.cs:103） | ✅ 满足（`common.delete` 漏词与硬编码中文见 P2-2/3） |

**规格轴额外核对（非 AC 但属项目承诺）**：ADR-1~ADR-9 全部落实，其中 ADR-2（跳过验证连接级、显式、默认关）在表单与 badge（MainWindow.xaml:40-44 `SslSkipped`）均有体现；ADR-7（历史 100 条）有测试；ADR-9（简易构建器子集）与 Java 的 `ESQueryBuilder` 对照成立（must/should/must_not/filter × term/match/wildcard/prefix/range/exists）。唯一与文档目标冲突的是 ADR 未声明「update by query 需 script」，而 AC-6 承诺了该功能（P0-1）。

---

## 4. 结论：必须修复的 blocker 清单

**P0（阻断，必须修复后交付）—— 1 项：**

1. **「按查询更新」为静默空操作**（`SearchViewModel.cs:309-313`，`EsClient.cs:129-130`）
   - 现状：`_update_by_query` 请求体仅含 `query` 无 `script`，匹配文档不产生任何变更，UI 却弹确认框并 Toast「操作成功」→ **假成功误导用户**，且 AC-6 “update/delete by query” 的 update 半边实质未交付。
   - 修复最小集：为搜索页提供 script 输入（或允许提交用户编辑的完整 DSL 体），并在无 script 时禁止走更新流程；修复后需补充一个「update body 含 script」的单测（当前 27 个单测没有覆盖构建器产物，建议一并补 `BuildDsl` 的 term/range/exists 与 script 场景断言）。

**P1（应修复，建议随交付一并处理）：** 暂无其它 P1。

**P2（建议，不阻断交付）：** 见标准轴 §2 P2-1 ~ P2-16，优先级最高的三条：REST 历史空态绑定失效（P2-1）、`common.delete` 漏词与硬编码中文（P2-2/3）、SQL cursor 不关闭（P2-4）。另建议交付前完成：清理 `$(MSBuildThisFileDirectory).packages` 残留目录与 `git init`（P2-15）。