# SPEC — ES-King 借鉴改造 + 现代精致 UI

## 目标（用户原始诉求）

1. **查看 `dsh-workspace/ES-King-wails` 源码，参考其设计优点**，改造我们（WPF 版 elastic-desktop-manager）**没有的功能**或**需要优化的地方**。
2. **我们的 WPF UI 不够精致**，用「现代精致 UI」理念改造界面。

## 背景事实（已核实，勿凭猜测推翻）

- 本项目：`/home/kiki/dsh-workspace/elastic-desktop-manager-wpf`，.NET 8 WPF，3 层：
  `ElasticDesktopManager.Core`（net8.0，无 UI 依赖，**可在 Linux 单测**）/ `ElasticDesktopManager`（WPF，net8.0-windows）/
  `ElasticDesktopManager.Tests`（控制台断言）/ `tests/binding-guard`（静态 XAML 守卫）。
- **环境限制（关键）**：开发机是 Linux，**无法运行 WPF GUI**。验收方式只能三选：
  ① `dotnet build` 零警告零错误；② Core 单测；③ `tests/binding-guard` 静态守卫。
  任何"跑起来看看"的验收标准都**不可执行**，不得写入 AC。
- 参考项目 ES-King-wails：Go + Wails + Vue3 + Naive UI，后端 `app/backend/service/es.go`（2079 行，~70 个 REST 端点封装）。
- 当前主题：`Themes/Dark.xaml`、`Themes/Light.xaml` 各 21 行（纯色刷子），`Themes/Common.xaml` 197 行（基础控件样式）。
- 当前主窗口：单一左侧 170px 文字 ListBox 导航，顶栏 48px，状态栏 28px。
- 现有 i18n：`Localization.cs` 双词典（zh_CN / en），**必须保持严格对齐**（binding-guard 会校验）。
- 现有测试基线：Core 单测 **48 通过**；binding-guard **3 通过**。

## 范围

两条并行工作流，**必须都交付**：

### 工作流 A：借鉴 ES-King 的功能补齐（按价值排序，本轮全做）

参考项目有、我们没有的能力。**优先级从高到低**：

| # | 功能 | 参考实现 | 我们应做什么 |
|---|---|---|---|
| A1 | **集群指标 (Stats)** | `GetStats()` → `_nodes/stats`，前端 `Core.vue` 用 `flattenObject` 拍平后按前缀分组、可折叠展示，每行显示「中文说明 + 值 + 原始 key」 | 新增「指标」页：拉 `_nodes/stats`，**扁平化 + 按前缀分组**，可折叠，值带单位格式化 |
| A2 | **索引 Mapping / Settings 在线查看与动态更新** | `GetIndexMappings` / `UpdateIndexMappings` / `GetIndexSettings` / `UpdateIndexSettings` | 索引页增加 Mapping/Settings 查看与编辑 |
| A3 | **分词调试 (Analyze)** | `AnalyzeText(index, text, analyzer, field)` → `_analyze` | 新增「分词」工具：输入文本+分词器，展示 token 序列 |
| A4 | **字段 Top 值 / 基数统计** | `GetFieldTopValues(index, field, size)` → terms + cardinality 聚合 | 索引页或独立入口：选字段看 Top N 分布 |
| A5 | **别名管理** | `GetIndexAliases` / `AddIndexAlias`(含 filter/routing) / `RemoveIndexAlias` | 索引页别名查看 + 增删 |
| A6 | **Reindex 数据迁移** | `Reindex(source, dest, query)`，异步 task | 工具入口：源→目标 + 可选 query |
| A7 | **索引模板查看/创建/删除** | `GetIndexTemplates` / `CreateIndexTemplate` / `DeleteIndexTemplate` / `GetComponentTemplates` | 新增「模板」页 |
| A8 | **诊断：分片分配解释 / 热点线程 / 线程池 / 挂起任务** | `ExplainAllocation` / `GetHotThreads` / `GetThreadPool` / `GetPendingTasks` | 扩展「诊断」能力（可分页签） |
| A9 | **强制段合并 Force Merge** | `MergeSegments(index)` | 索引页加操作项 |
| A10 | **索引数据导出 JSON / 批量导入** | `DownloadESIndex`（带 DSL 过滤）/ `BulkImport` | 导出/导入本地文件 |

> 若时间/复杂度超预算，**A1/A2/A3/A5 为必做**，其余可裁剪——但任何裁剪必须在 QA.md 明示。

### 工作流 B：现代精致 UI 改造

「现代精致」的**可检验定义**（避免主观空谈）：

| # | 维度 | 具体做法 |
|---|---|---|
| B1 | **设计令牌** | 引入统一 spacing / radius / fontSize / shadow 令牌；颜色从 21 行裸刷子升级为完整语义色板（含 hover/active/subtle/overlay 分层） |
| B2 | **导航** | 文字 ListBox → **图标 + 文字**导航，选中态用强调色左侧指示条 + 淡背景；悬停动效 |
| B3 | **层级与留白** | 统一卡片圆角/内边距/间距节奏；页面标题区统一（标题 + 副标题 + 操作区） |
| B4 | **控件精修** | 按钮/输入框/下拉/复选/滚动条/DataGrid 全套自绘模板：圆角、focus 态、禁用态、悬停过渡 |
| B5 | **状态反馈** | 空态、加载态、错误态视觉统一；Toast 精致化（圆角/阴影/图标） |
| B6 | **连接页** | 参考 ES-King 的**卡片式连接列表**：卡片悬停抬升、当前连接状态徽章 |
| B7 | **浅色/深色** | 两套主题色板均需重新设计并保持对比度（正文对比度 ≥ 4.5:1） |

## 可测验收标准（AC）

**每条都必须能在 Linux 上机械验证。**

### 功能类

- **AC1**：`EsClient` 新增以下方法且均有单测覆盖（用 `FakeHandler` 断言**请求方法 + 路径 + 请求体**）：
  `_nodes/stats`、`_analyze`、别名增删查、`_reindex`、`_forcemerge`、模板查删、`_cluster/allocation/explain`、`_nodes/hot_threads`、`_nodes/thread_pool`、`_cluster/pending_tasks`、`_mapping` 读写、`_settings` 读写、字段 Top 值聚合。
- **AC2**：新增**指标扁平化 + 分组**逻辑放在 Core 且纯函数可测：
  输入嵌套 JSON → 输出 `(分组名, 中文说明, 值, 原始key)` 列表；覆盖
  ① 深层嵌套拍平；② 数组按索引展开；③ 分组顺序稳定；④ 值格式化（bytes→人类可读、ms、百分比）。
- **AC3**：所有新增 REST 调用路径**不得出现双斜杠或漏斜杠**（回归：源 Java 项目曾漏 `/`）——用测试断言路径字符串。
- **AC4**：新增功能全部经 `Localization`，**zh/en 词条严格对齐**（binding-guard 通过）。

### UI 类（静态可验）

- **AC5**：`Themes/Common.xaml` 覆盖以下控件模板，且每个都有 **hover / focus / disabled** 三态：
  `Button`、`TextBox`、`PasswordBox`、`ComboBox`、`CheckBox`、`RadioButton`、`ScrollBar`（含 `ScrollViewer` 纵向）、`DataGrid`、`DataGridColumnHeader`、`ListBoxItem`、`TreeViewItem`、`TabItem`。
- **AC6**：导航项模板包含**图标与文字两部分**（XAML 中存在图标绑定），选中态有**左侧强调指示条**。
- **AC7**：设计令牌以 `x:Key` 资源形式集中定义，页面中**不得出现硬编码 `#RRGGBB`**（新改动的 XAML 内），必须走 `DynamicResource`。
- **AC8**：zh_CN / en 词条数**完全相等**且无单边缺失（binding-guard 现有检查 + 断言总数）。
- **AC9**：**binding-guard 全绿**：XAML 中不存在「只读属性 + 默认 TwoWay 目标」的绑定。
  ⚠️ 本轮会大量改 XAML，这是**最容易翻车**的一条——任何 `TextBox.Text` 绑到 `private set` 属性都会在运行时抛异常而编译为零错误。
- **AC10**：`dotnet build ElasticDesktopManager.sln` → **0 Warning / 0 Error**。
- **AC11**：Core 单测 **≥ 48 全通过**（新增用例后总数只增不减）。
- **AC12**：Core 中**不新增任何 WPF 依赖**（`ElasticDesktopManager.Core.csproj` 无 `UseWPF`，且不引用 `System.Windows.*`）——保证 Linux 可测。

## 明确不做 / 已知取舍

- 不移植自动更新（原项目已有取舍记录）。
- 快照/SLM、ILM、文档完整 CRUD 属**大块功能**，列为后续批次（本轮 A1–A10 已足够）。
- 仍为 Windows-only（WPF 本质），Linux 侧只做编译 + 静态 + Core 单测验证。
- 视觉最终确认需用户在 Windows 上人工核对（我们会给出核对清单）。

## 交付物

- 代码改动（Core + WPF + Tests + binding-guard）。
- `docs/team/` 下 SPEC / ARCHITECTURE / TASKS / REVIEW / QA / context。
- 更新 README（新功能 + UI 说明 + 截图核对清单）。
