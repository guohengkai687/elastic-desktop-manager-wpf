# TASKS — 拆解与状态

> 状态：`TODO` / `DOING` / `DONE` / `CUT`（裁剪必须写明理由，并在 QA.md 复述）
> 每个 T 都是**独立可验证**的：做完就能跑一个命令给出证据。

## 阶段 0：基线与护栏

| ID | 任务 | 状态 | 验证 |
|---|---|---|---|
| T0.1 | 记录基线：build / 单测 48 / guard 3 | DONE | 已有记录 |
| T0.2 | 修复或确认 binding-guard 对本轮新增 XAML 的覆盖 | DONE | guard 全绿 |

## 阶段 1：Core 能力层（无损、可单测，先行）

> 全部在 `ElasticDesktopManager.Core`，**不引入任何 WPF 依赖**（AC12）。

| ID | 任务 | 对应 | 状态 | 验证 |
|---|---|---|---|---|
| T1.1 | `EsClient` 新增集群指标：`GetNodeStatsAsync()` → `GET /_nodes/stats` | A1 | DONE | 单测断言路径 |
| T1.2 | `EsClient` 新增分词：`AnalyzeTextAsync(index, field, analyzer, text)` → `POST /{index}/_analyze` | A3 | DONE | 单测断言路径+body |
| T1.3 | `EsClient` 新增 Mapping：`GetMappingAsync(index)` / `PutMappingAsync(index, json)` | A2 | DONE | 单测 |
| T1.4 | `EsClient` 新增 Settings：`GetSettingsAsync(index)` / `PutSettingsAsync(index, json)` | A2 | DONE | 单测 |
| T1.5 | `EsClient` 新增别名：`GetAliasesAsync` / `AddAliasAsync(alias, filter, routing)` / `RemoveAliasAsync` | A5 | DONE | 单测 |
| T1.6 | `EsClient` 新增 `ReindexAsync(source, dest, query)` → `POST /_reindex` | A6 | DONE | 单测 |
| T1.7 | `EsClient` 新增 `ForceMergeAsync(index)` → `POST /{index}/_forcemerge` | A9 | DONE | 单测 |
| T1.8 | `EsClient` 新增模板：`GetTemplatesAsync` / `GetComponentTemplatesAsync` / `DeleteTemplateAsync` / `CreateTemplateAsync` | A7 | DONE | 单测 |
| T1.9 | `EsClient` 新增诊断：`ExplainAllocationAsync` / `HotThreadsAsync` / `ThreadPoolAsync` / `PendingTasksAsync` | A8 | DONE | 单测 |
| T1.10 | `EsClient` 新增字段 Top 值：`FieldTopValuesAsync(index, field, size)` → terms + cardinality | A4 | DONE | 单测 |
| T1.11 | 新增 `EsMetricsFlattener`（纯函数）：嵌套 JSON → 分组指标行；值格式化（bytes/ms/percent） | A1/AC2 | DONE | 单测 ≥6 例 |
| T1.12 | 路径合法性回归：所有新路径无 `//`、无漏 `/` | AC3 | DONE | 单测 |

## 阶段 2：设计令牌与主题（UI 地基）

> 先立地基，再改页面——否则页面会各写各的硬编码色值（AC7 会挂）。

| ID | 任务 | 对应 | 状态 | 验证 |
|---|---|---|---|---|
| T2.1 | 重写 `Themes/Dark.xaml`：完整语义色板（surface 分层 / border / text 分层 / accent / 语义色 / overlay） | B1/B7 | DONE | build |
| T2.2 | 重写 `Themes/Light.xaml`：与 Dark **同 key 全集** | B1/B7 | DONE | build + key 对齐检查 |
| T2.3 | 新增 `Themes/Tokens.xaml`：spacing / radius / fontSize / shadow 几何令牌（StaticResource） | B1 | DONE | build |
| T2.4 | 重写 `Themes/Common.xaml`：全套控件模板 + hover/focus/disabled 三态 | B4/AC5 | DONE | build |
| T2.5 | 主题字典合并顺序与运行时切换验证（换主题不崩、无缺失 key） | B7 | DONE | 静态检查 key 覆盖 |

## 阶段 3：壳层与导航（B2/B3/B6）

| ID | 任务 | 对应 | 状态 | 验证 |
|---|---|---|---|---|
| T3.1 | `NavItem` 增加 `Glyph`（图标）；保持 `Code`/`TitleKey`/`Title` 契约不变 | B2 | DONE | Core 单测 |
| T3.2 | `MainWindow` 顶栏精修：品牌区 / 连接选择器 / 图标按钮统一尺寸与悬停 | B3 | DONE | build |
| T3.3 | 左侧导航改为图标+文字，选中态左侧强调指示条 | B2/AC6 | DONE | guard + 人工核对清单 |
| T3.4 | 状态栏 + Toast 精修（圆角/阴影/图标） | B5 | DONE | build |
| T3.5 | `ConnectionView` 改卡片式列表 | B6 | **未做**（保留文件夹层级，避免丢失树形语义） | — |

## 阶段 4：新页面与功能接线

| ID | 任务 | 对应 | 状态 | 验证 |
|---|---|---|---|---|
| T4.1 | 新增「指标」页（VM + View + 导航项 + i18n），可折叠分组 | A1 | DONE | guard + 单测 |
| T4.2 | 新增「诊断」页：分片分配解释 / 热点线程 / 线程池 / 挂起任务（页签） | A8 | DONE | guard |
| T4.3 | 新增「模板」页：索引模板 / 组件模板 查看与删除 | A7 | DONE | guard |
| T4.4 | 新增「分词」工具：文本 + 分词器 → token 列表 | A3 | DONE | guard |
| T4.5 | 索引页扩展：Mapping/Settings 查看与更新 | A2 | DONE | guard |
| T4.6 | 索引页扩展：别名管理（增/删/带 filter·routing） | A5 | DONE | guard |
| T4.7 | 索引页扩展：Force Merge 操作 | A9 | DONE | guard |
| T4.8 | 索引页扩展：字段 Top 值查看 | A4 | DONE | guard |
| T4.9 | 工具入口：Reindex 数据迁移 | A6 | DONE | guard |
| T4.10 | 索引数据导出 JSON（带 DSL 过滤） | A10 | **未做**（Core 未实现） | — |
| T4.11 | 批量导入（本地 JSON → `_bulk`） | A10 | **未做**（Core 未实现） | — |

## 阶段 5：既有页面视觉统一（B3/B5）

| ID | 任务 | 状态 | 验证 |
|---|---|---|---|
| T5.1 | 统一页面标题区（标题+副标题+操作区）到所有页面 | DONE | guard |
| T5.2 | 空态/加载态/错误态视觉统一 | DONE | guard |
| T5.3 | DataGrid / 表格精修（行高、悬停、选中、表头） | DONE | build |
| T5.4 | 各页面硬编码色值清理 → DynamicResource（AC7） | DONE | guard 新规则 |

## 阶段 6：i18n 与测试护栏

| ID | 任务 | 状态 | 验证 |
|---|---|---|---|
| T6.1 | 新增词条（zh/en 成对），保持严格对齐 | DONE | guard |
| T6.2 | binding-guard 新增规则：zh/en **词条数量相等** | DONE | guard |
| T6.3 | binding-guard 新增规则：XAML 无硬编码 hex 颜色 | DONE | guard + 自检 |
| T6.4 | binding-guard 新增自检：上述两条规则**能真的失败**（喂违规样本） | DONE | guard |
| T6.5 | Core 单测补齐 A1–A10（AC1/AC2/AC3） | DONE | 单测 |

## 阶段 7：文档

| ID | 任务 | 状态 |
|---|---|---|
| T7.1 | README：新功能表 + UI 说明 + Windows 人工核对清单 | DONE |
| T7.2 | `docs/team/` 全部产物归档 | DONE |

## 门禁

- **G1**（阶段 1 后）：Core 单测全绿且总数 ≥48；Core 无 WPF 依赖。
- **G2**（阶段 2 后）：build 0/0；Dark/Light key 全集一致；Common.xaml 控件三态齐备。
- **G3**（阶段 4 后）：guard 全绿（含新规则）；所有新页面走 DynamicResource。
- **G4**（交付前）：build 0 警告 0 错误 + 单测全绿 + guard 全绿；REVIEW 无未处置 blocker。

---

## 最终结果（本轮）

- **已完成 43 项**：阶段 0–2 全部；阶段 3 除 T3.5；阶段 4 的 T4.1–T4.9；阶段 5 全部；阶段 6 全部；阶段 7 全部。
- **未完成 3 项**（已在 ARCHITECTURE.md 与 QA.md 如实声明，未掩饰为已完成）：
  - T3.5 连接页卡片化（B6）—— 现为树形列表，保留文件夹层级语义。
  - T4.10 / T4.11 导出与导入（A10）—— Core 未实现。
- **门禁**：G1 ✅（Core 单测 99 全绿、Core 无 WPF 依赖）；G2 ✅（build 0/0、Dark/Light key 一致、控件三态齐备）；
  G3 ✅（守卫 8/8 含新规则）；G4 ✅（build 0/0 + 单测 99/99 + 守卫 8/8，REVIEW 无未处置 blocker）。

## 期间新增的守卫任务（原计划外，因发现真实缺陷而补）

| ID | 任务 | 状态 | 触发原因 |
|---|---|---|---|
| T6.6 | 守卫规则：引用的资源 key 必须已定义 | DONE | 发现 `FindResource("TextBrush")` 缺失会导致运行期崩溃 |
| T6.7 | 守卫自检三方向（required 识别 / 同名不误报 / 唯一只读仍抓） | DONE | 守卫类型盲查找产生误报，需防"改废守卫" |
| T6.8 | 守卫规则：Dark/Light 颜色 key 一致 | DONE | 缺 key 会导致该主题下元素静默不可见 |


---

# 第 3 轮（用户 5 项要求）

## DONE

- [x] 移除 分词 / 诊断 / 模板 三个功能（视图、VM、导航、i18n、Core 端点与解析器/模型、12 个测试）
- [x] 新增快照管理：`_snapshot` 仓库（列出/新建/校验/删除）+ 快照（列出/创建/删除/恢复）+ 状态查询
- [x] 快照页 UI（仓库卡片 + 创建快照表单 + 快照表格 + 状态圆点），6 个端点/解析测试 + 3 个健壮性测试
- [x] 搜索页：索引下拉改为可编辑（支持通配符/别名/多索引）；加载失败不再静默，改为显示在下拉右侧
- [x] 修复连接管理页「连接」「测试」按钮永久禁用（`Selected` setter 漏通知 `CanConnect`）
- [x] 修复 `ThemeService` 误删 Common.xaml（切主题时整套控件模板被移除 → UI 变回 WPF 默认外观）
- [x] 跟随系统主题：`SystemEvents.UserPreferenceChanged` 订阅 + 手动切换时自动关闭 AutoTheme
- [x] UI 重新设计：矢量图标系统（24 个图标 + Core 侧语法校验 + Python 渲染复核）、导航分组、
      主题性格令牌（浅色精致 / 深色工具）、首页与快照页改版、页面刷新按钮统一为矢量图标
- [x] 守卫 10 → 13 项（新增：派生属性通知、资源字典字面量下标、令牌类型扩展），全部含可失败自检
- [x] 文档更新（README / ARCHITECTURE ADR-7·ADR-8·R10-R12 / QA 第 3 轮 / 本文件）

## 未做（如实声明）

- [ ] 其它页面（节点/分片/索引/REST/SQL/搜索/指标）的刷新按钮已换矢量，但页面内部仍有少量文本符号
      （如「+ 新增条件」「✕ 删除」），未逐一到矢量图标。
- [ ] 逐条指标中文说明表：仍维持 ADR-1 的取舍（分组名本地化 + 原始 ES key）。
- [ ] 快照的 SLM 策略（`_slm/policy`）、ILM、索引数据导出/导入（A10）未做。
- [ ] 未在本机运行 GUI 验证（Linux 无 WPF）；主题切换、图标渲染、两套性格差异需在 Windows 上复验
      （见 QA.md 第 11-14 条）。

---

## 第 4 轮（用户 3 项要求）

### 已完成

- [x] 修复深色主题**页面区白底**：隐式 `TargetType="Window"` 样式对派生窗口不生效（dotnet/wpf#10461）→
      抽出带 key 的 `WindowBaseStyle`，**11 个窗口**（MainWindow + 10 个弹窗）显式引用；MainWindow 另直写 `Background`
- [x] 修复搜索页**索引下拉无数据**：ComboBox 模板补 `PART_EditableTextBox` + `IsEditable` 触发器；
      同时删掉会与 `IsOpen` 绑定打架的 `HasItems=False → 强制关 Popup` 触发器
- [x] 索引列表加载改为**不静默**：显示「N 个索引 / 该集群没有索引 / 错误原因」，并新增下拉旁的手动刷新按钮；
      解析逻辑下沉到 Core（`ParseIndexNames` + `EsClient.IndexNamesFormat`）以便单测
- [x] 快照页重构为 **5 个页签**：仓库管理 / 快照管理 / 快照恢复 / 自动策略 SLM / 生命周期 ILM
- [x] Core 新增端点：`/_slm/policy`（列出/新建/删除/立即执行）、`/_ilm/policy`（列出/新建/删除）、
      `GET /_recovery?active_only=true`、`GET /_snapshot/{repo}/{snap}`；恢复支持 `rename_pattern`/`rename_replacement`
- [x] Core 新增解析器与模型：`EsSlmPolicy`、`EsIlmPolicy`、`EsRecoveryShard`（含 7.x 对象 / 8.x 字符串双形态兼容）
- [x] 每页签**独立状态行**（`TabStatus`：正常次要色 / 错误危险色），SLM/ILM 不支持的集群只在对应页签内报错
- [x] i18n 新增 88 个词条（zh/en 严格对齐，守卫强制）
- [x] 守卫 13 → **18 项**：可编辑 ComboBox 部件、i18n key 存在性（两遍扫描）、zh/en 占位符一致性、窗口必须套 WindowBaseStyle，均做负向验证；
      新规则**发现并修复 2 处历史遗留**（`common.save` / `common.add` 从未定义）
- [x] 测试 96 → **102**：索引名解析、SLM/ILM/恢复端点契约、恢复重命名、SLM 解析、ILM 解析、恢复进度解析
- [x] 文档更新（README / ARCHITECTURE ADR-9·ADR-10·R13-R15 / QA 第 4 轮 / 本文件）

### 未做（如实声明）

- [ ] ILM 的 `start`/`stop`/`status`、SLM 调度器状态未接入 UI（客户端方法已一并删除，不留死代码）。
- [ ] 快照仓库类型下拉支持 s3/gcs/azure，但设置项只有 location/compress（其余需手写 JSON）。
- [ ] 未能确认第 1、2 项修复在真机上的视觉/交互效果：Linux 无 WPF，需在 Windows 上复验 QA.md 第 15-19 条。
- [ ] `_cat/nodes` 等 URL 里的 `node.role` 这类字段名不做本地化（ES 术语，见 ADR-10 边界）。

---

## 第 5 轮（用户截图缺陷 + 独立 Core/ES 复审）

### 已完成

- [x] **用户截图缺陷**：搜索页「添加条件」两个下拉收起态显示 `ClauseOption`/`OperatorOption`（被列宽截断成
      `ClauseOp`/`OperatorOp`），展开列表却正常 → 自写 ComboBox 模板漏
      `ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"`（`DisplayMemberPath` 实为 `ItemsControl`
      装到 `ItemTemplateSelector` 上的内部选择器，`ComboBox.cs` 里根本没有这个属性）。
      只改一行，**一并修好全项目 6 个 `DisplayMemberPath` 下拉**（设置页语言、快照页仓库×2、搜索页条件行×2）
- [x] **ILM「修改时间」列恒为裸毫秒**（`1718452800000`）：`modified_date` 是 `declareLong`，ISO 在同级
      `modified_date_string` → 改 `TimestampOf(el, "modified_date", "modified_date_string")`
- [x] **ILM 阶段链顺序不可信**（`Collectors.toMap` 的 `HashMap` 顺序）→ 按 `ORDERED_VALID_PHASES` 稳定排序，
      未知阶段殿后并保持 ES 相对顺序
- [x] **SLM 成功/失败记录的时间伴随字段名读错**（`time_millis` → 真实字段 `time_string`）
- [x] 测试改为**真实响应形状**：ILM fixture 用毫秒 + `_string` 伴随字段、phases 顺序打乱；
      断言从"非空"改为**正向钉死格式化结果**（格式 + 数值对应瞬间）
- [x] 守卫 18 → **21 项**：新增「ComboBox 收起态展示器必须绑 `ContentTemplateSelector`」、
      「DataGrid 列数 ↔ 表头映射项数」，各配自检 + 真实文件负向验证 + **规则自保护**（找不到保护对象要报错，不许空转通过）
- [x] 独立复审意见逐条回代码核实：**7 条成立、1 条不成立（上一轮已修，属重复）、10 条已在 `7a0b135` 修掉**（详见 REVIEW.md）
- [x] **纠正上一轮文档笔误**：快照页列数为 `3/7/9/9/5`（原写 `3/7/9/8/5`），改由守卫机械保证
- [x] 文档更新（ARCHITECTURE ADR-11·ADR-12 + R16-R18 / QA 第 5 轮 + 核对清单 20-24 / REVIEW 第 5 轮 / 本文件）

### 未做（如实声明）

- [ ] 真机视觉复验仍需用户在 Windows 上完成：QA.md 第 15-19 条（第 4 轮）+ **20-24 条（本轮：下拉收起态、ILM 时间列/阶段顺序、表头齐全、语言切换）**。
- [ ] 复审报告里"本机无法验证"的部分继续如实保留：自写 ComboBox 模板的编辑态/z-order/命中测试、ES 真实响应形状（本机无 ES，只有手写 fixture）。
- [ ] 已接受的设计限制不变：解析时格式化的文本（保留/统计/分片）缓存在模型上，切语言后需下次刷新才更新（ADR-10 代价）。
- [ ] ILM `start`/`stop`/`status`、SLM 调度器状态、非 fs 仓库的完整 settings 表单仍未接入。

---

## 第 6 轮（搜索分页）

### 已完成

- [x] **服务端分页**（用户反馈"总命中 2570 只显示 10 条"）：DSL 写入 `from=(页号-1)×每页条数` 与 `size`，
      对齐源项目 `ClusterSearchController` + `PagingControl` 的行为
- [x] Core 新增 `SearchPaging` 纯函数（`FromOf`/`TotalPages`/`ClampPage`/`ExceedsWindow`/`HasNext`）
      + `EsQueryHelper.WithPaging`（注入并覆盖 from/size，保留原 query）+ `EsSearchResult.TotalHitsIsLowerBound`
- [x] UI 分页条：共 N 条 · 每页条数下拉（10/20/30/50/100）· 第 x / y 页 · 首页/上页/下页/末页 · 前往 [ ] 页（回车提交）
- [x] 结果窗口上限（`MaxFrom = 5000`，ES `index.max_result_window` 默认 10000）：超限**发请求前**给可读错误
- [x] `hits.total.relation == "gte"` 显示为下限 `10000+`，且不因此过早禁用"下一页"
- [x] 有意优于源项目的三点：点搜索回到第 1 页、结果集变小自动收敛页码并重查一次、请求代次号丢弃过期响应
- [x] 新增 3 个图标（`ChevronLeft`/`PageFirst`/`PageLast`）并登记进 `AppIcons.All`
- [x] i18n 新增 11 个词条（zh/en 对齐由守卫强制）
- [x] 测试 102 → **105**（`relation=gte`、分页数学、DSL 注入），全部做负向验证
- [x] 守卫 21 → **23 项**：新增「页面视图 code-behind 本地化必须订阅 `LanguageChanged`」+ 自检
- [x] 顺带修复 **SearchView 不随语言切换**（分页条文案就在这个视图里，不修等于新功能一上线就是坏的）
- [x] 文档更新（ARCHITECTURE ADR-13 + R19·R20 / QA 第 6 轮 + 核对清单 25-29 / README / 本文件）

### 未做（如实声明）

- [x] ~~**8 个缓存页面视图不随语言切换**（首页/节点/分片/索引/指标/REST/SQL/空态视图）~~ → **第 7 轮已修**（见下）
- [x] ~~**31 个 DataGrid 列头硬编码英文**~~ → **第 7 轮已修**，并**更正了本条描述的错误**（见下）
- [ ] 真机复验仍需用户在 Windows 上完成：QA.md 第 15-19 条（第 4 轮）、20-24 条（第 5 轮）、**25-29 条（第 6 轮）**。
- [ ] 深分页（from 很大）的服务端开销是 ES 自身性质：本实现只做上限提示，未引入 PIT/`search_after` 游标翻页。

---

## 第 7 轮（语言切换收尾：8 个缓存页面 + 文案唯一来源）

用户指令："接着把那 8 个页面的语言切换"。即第 6 轮登记的那批 i18n 债务。

### 已完成

- [x] **责任划分定案（ADR-14）**：订阅点只放在**视图**侧，VM 不挂静态事件
      （`IndexToolsViewModel` 每次开窗都新建，写在基类构造里会让静态事件把已关闭的窗口永久持有）；
      `PageViewModelBase` 新增 public `Relocalize()` + `protected virtual OnRelocalize()`
- [x] **8 个页面视图全部修完**：`EmptyStateView`/`HealthView`/`IndicesView`/`MetricsView`/`NodesView`/`RestView`/`ShardsView`/`SqlView`
      —— 抽出 `Localize()`、构造时订阅 `LanguageChanged`、处理器里 `Localize() + VM.Relocalize()`
- [x] **VM 侧缓存文案统一重算**：指标卡标签、节点/分片/指标摘要、索引总数与页码、SQL 摘要与页码、
      搜索页索引下拉提示（`IndexHint`，第 6 轮漏掉的）、快照页五条状态行（改为覆写 `OnRelocalize`）
- [x] `KnownStalePageLocalizers` **债务清单清零**（棘轮机制保留：以后新增页面再犯直接报错）
- [x] **文案唯一来源**：XAML 清掉 33 处写死的 `Header=`、7 处写死的 `ToolTip=`；
      `StringFormat=Total: {0}` 改为词条 `index.total`（中英界面都显示 `Total:` 的真实缺陷）
- [x] 表命名统一为 `XxxGrid`/`XxxHeaders`，让"列数 ↔ 表头映射"规则覆盖**全部 9 张表**（原先只保护 SnapshotView）
- [x] **修掉索引页 7 处双重翻译**（`CreateConfirm`/`ShowJson` 收 key 却传了 `L()` 的结果）：
      中文界面看着正常，**英文界面确认框/JSON 窗标题仍是中文**
- [x] 顺带修：`NodesView`/`ShardsView` 的 `SummaryText` 从未绑定（"节点统计：N"从未显示过）；
      `HealthView`/`IndicesView`/`NodesView`/`ShardsView` 的刷新按钮提示；`MainWindow` 三个图标提示；
      `RestHistoryWindow` 的 `Method` 列
- [x] 守卫 **23 → 29 项**（3 条新规则 + 3 条自检）+ 修掉一处守卫自身的判据缺陷
      （`x:Name="Grid"` 也以 `Grid` 结尾，会报成"找不到 Headers"而不是命名问题）
- [x] 测试 105/105、构建 0 警告 0 错误；**负向验证 7 条**全部按预期精准报错
- [x] 文档更新（ARCHITECTURE ADR-14 + R21-R23 / QA 第 7 轮 + 核对清单 30-36 + 更正旧描述 / 本文件 / README）

### 更正上一轮的描述错误（如实记录）

第 6 轮 QA/TASKS 写的"31 个 DataGrid 列头硬编码英文 → 中文界面中英混排"**部分是错的**：
`NodesView`/`ShardsView`/`IndicesView` 的表头在 `Loaded` 时已按词条赋成中文，中文界面本来就是中文，
XAML 里的英文只是被覆盖的占位（性质是"两处来源"，不是"界面上有英文"）。计数也应为 **33 处**
（13+8+9+3）。真正用户可见的英文是：4 处 `ToolTip="Refresh"`、`MainWindow` 3 个提示、
`RestHistoryWindow` 的 `Method` 列、`IndicesView` 的 `StringFormat=Total:` —— 均已在第 7 轮修复。

### 未做（如实声明）

- [ ] 真机复验仍需用户在 Windows 上完成：**QA.md 第 30-36 条（本轮）**以及 15-19、20-24、25-29 条。
- [ ] `PageViewModelBase` 的"子类漏算某个缓存属性"守不住：守卫只强制"视图调了 `Relocalize()`"，
      新增一个加载时拼好的 VM 文案必须人工记得写进 `OnRelocalize()`（已记入 ADR-14 的纪律条款）。
- [ ] 解析时格式化的 ES 文本（保留/统计/分片，缓存在模型上）仍需下次刷新才更新（ADR-10 的既有代价）。
- [ ] 深分页、ILM/SLM 调度器状态、非 fs 仓库设置表单：同第 6 轮，未做。
