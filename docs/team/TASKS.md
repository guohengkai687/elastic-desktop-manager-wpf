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
