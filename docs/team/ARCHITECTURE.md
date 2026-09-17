# ARCHITECTURE.md — ES-King 借鉴改造 + 现代精致 UI

> 本轮由**主代理串行承担架构师角色**（子代理两次启动后均以无产出失败告终，见 `context.md` 的降级记录）。
> 内容与已实现代码一一对应，可直接据此继续开发。

## 总览

两条工作流并行推进，均落在既有三层结构内：

```
Core (net8.0，无 UI 依赖，Linux 可单测)      ← 能力层：端点封装 + 纯函数解析/扁平化
  ├── Es/EsClient.Operations.cs             ← A1–A9 的 REST 端点（partial class 扩展）
  ├── Es/EsMetricsFlattener.cs              ← 指标扁平化+分组+值格式化（纯函数）
  ├── Es/EsParsers.cs                       ← 新增 分词/模板/字段Top值/别名 解析
  ├── Es/EsQueryHelper.cs                   ← 新增 Mapping/Settings 提交体提取
  └── Models/OperationsModels.cs            ← AnalyzeToken/EsTemplate/EsAlias/FieldTopValue

WPF (net8.0-windows)                        ← 表现层
  ├── Themes/Tokens.xaml                    ← 几何令牌（StaticResource）
  ├── Themes/Dark.xaml / Light.xaml          ← 语义色板（DynamicResource，key 全集一致）
  ├── Themes/Common.xaml                     ← 全套控件模板（三态）
  ├── Views/{Metrics,Analyze,Diagnostics,Templates}View
  └── Views/IndexToolsWindow                ← 索引工具对话框
```

---

## ADR-1：指标扁平化与分组

**决策**：在 Core 实现纯函数 `EsMetricsFlattener`，而不是在 ViewModel 里做。

**签名**
```csharp
public sealed record MetricRow(string Group, string Key, string Value, string? Node = null);

IReadOnlyList<MetricRow> FlattenNodeStats(string json)   // 专用于 _nodes/stats（含多节点）
IReadOnlyList<MetricRow> Flatten(string json, string? node = null)  // 通用入口
string FormatBytes(long bytes)
string FormatDuration(long millis)
string FormatValue(string key, JsonNode? node)
IReadOnlyList<string> GroupsOf(IEnumerable<MetricRow> rows)
```

**规则**
1. 递归拍平嵌套对象，点分路径为 key；数组按 `[i]` 展开（不把整块 JSON 塞进一格）。
2. 空对象/空数组不丢弃，显示 `{ }` / `[ ]`，避免"指标消失了"的错觉。
3. 分组 = 路径首段；分组顺序按固定优先级表（cluster→nodes→indices→jvm→os→…），未知分组按首次出现顺序追加。**顺序稳定**（可测）。
4. 值格式化按 key 语义自动选择：`_in_bytes`/`_size_in_bytes` → 人类可读字节；`_in_millis`/`_time_in_millis` → 时长；`_in_seconds` → 时长；布尔/字符串原样；空串显示 `-`。
5. 非法 JSON 返回空列表而非抛异常（面板类 UI 不应因脏数据崩）。

**取舍（明确记录）**：**未**为每个 ES 指标 key 提供逐条中文说明。
ES-King 有一张庞大的 `getLabel(key)` 映射表；我们要维护 zh/en 双份，且必然覆盖不全，
会出现"一半中文一半英文"的割裂观感。改为：**分组名本地化**（`metrics.group.*`）+ **原始 key**（符合 ES 官方文档用语）+ **人类可读的值** + **多节点时显示节点名**。
若后续确需逐条说明，可在 Core 增一张 `key → i18n key` 映射并按需回退，接口无需改动。

**性能**：单节点 `_nodes/stats` 常有数百行，多节点上千。因此分组**默认折叠**（首次进入只展开第一组），
并记忆用户展开状态；筛选时命中的分组自动展开。折叠的 `Expander` 不会实例化子树，避免创建上千可视元素。

---

## ADR-2：设计令牌系统

**决策**：几何令牌与颜色令牌分离。

| 类别 | 文件 | 引用方式 | 理由 |
|---|---|---|---|
| 几何（间距/圆角/字号/控件高/阴影/动效时长） | `Themes/Tokens.xaml` | `StaticResource` | 不随主题变化，无需运行时重算 |
| 颜色（语义色板） | `Themes/Dark.xaml` / `Light.xaml` | `DynamicResource` | 运行时切主题 |

**合并顺序（关键）**：`App.xaml` 中必须 **Tokens → Common → Light**。
Common 用 `StaticResource` 引用令牌，令牌字典必须先合并，否则解析失败（**编译期不报错**，运行期才炸）。

**间距**（4 的倍数）：`Space1..Space6` = 4/8/12/16/24/32
**圆角**：`RadiusXs/Sm/Md/Lg/Pill` = 3/5/8/12/999
**字号**：`FontXs..FontXxl` = 11/12/13/15/19/26
**控件高度**：`ControlHeight`=30、`ControlHeightSm`=26、`TitleBarHeight`=52、`StatusBarHeight`=28、`NavWidth`=208
**阴影**：`ShadowSm/Md/Lg`（卡片 < 浮层 < 对话框）
**等宽字体**：`MonoFont`（JSON/DSL 统一）

**⚠️ 令牌的声明类型必须与目标属性类型精确一致（曾导致"启动即崩"，勿改成统一 Double）**

| 令牌 | 声明类型 | 目标属性 | 类型写错的后果 |
|---|---|---|---|
| `Space1..6` | `Thickness` | `Padding` / `Margin` / `BorderThickness` | 运行期 `XamlParseException` |
| `TitleBarHeight` / `StatusBarHeight` / `NavWidth` | `GridLength` | `RowDefinition.Height` / `ColumnDefinition.Width` | 同上（**已实际发生，见 QA.md 缺陷 7**） |
| `FontXs..Xxl` / `ControlHeight(Sm)` | `sys:Double` | `FontSize` / `MinHeight` / `Height` | 类型恰好匹配 → 正确 |
| `RadiusXs..Pill` | `CornerRadius` | `CornerRadius` | 类型恰好匹配 → 正确 |

原因：WPF 的 `GridLengthConverter` / `ThicknessConverter` **只接受字符串，不接受数字**。
把上述令牌统一写成 `sys:Double`（"看起来更整洁"）是错误做法：编译期 0 错误 0 警告，真机运行才炸，
报错信息还是误导性的「`"52"`不是属性`"Height"`的有效值」——看起来像值错了，其实是类型错了。
由 `tests/binding-guard` 的「令牌类型匹配」规则静态拦截（内置属性→类型表，并能解析 `Setter` 的 `TargetType`）。
非均匀边距**不要**写 `Margin="0,{StaticResource Space2},0,0"`（XAML 会把整串当字面量），应另定义 `Thickness` 令牌。

**色板分层**（两主题 key 完全一致，由守卫强制）：

| 语义 | 用途 |
|---|---|
| `WindowBackgroundBrush` | 最底层窗口背景 |
| `SurfaceBrush` / `SurfaceAltBrush` / `SurfaceRaisedBrush` | 卡片 / 次层（表头、交替行）/ 浮层（弹窗、Tooltip） |
| `BorderBrush` / `BorderStrongBrush` / `SeparatorBrush` / `FocusRingBrush` | 常规描边 / 强调描边（悬停）/ 分隔线 / 焦点环 |
| `HoverBrush` / `HoverStrongBrush` / `PressedBrush` | 悬停 / 强悬停 / 按下 |
| `TextPrimaryBrush` / `TextSecondaryBrush` / `DimTextBrush` | 正文 / 次要 / 提示占位 |
| `AccentBrush` / `AccentHoverBrush` / `AccentFaintBrush` / `AccentSubtleBrush` / `AccentTextBrush` | 强调 / 悬停 / 淡底 / 更淡底 / 淡底上的可读强调文字 |
| `DangerBrush` / `GreenBrush` / `YellowBrush` / `RedBrush` / `GrayBrush` | 语义状态色 |
| `NavSelectedBrush` / `NavIndicatorBrush` | 导航选中底 / 选中指示条 |
| `OverlayBrush` / `OverlayTextBrush` | Toast / 遮罩 底与文字 |
| `ScrollThumbBrush` / `ScrollThumbHoverBrush` | 滚动条滑块 |
| `SelectedRowBrush` | 表格选中行 |

**对比度**（关键项）
- 浅色：正文 `#1B1F26` on `#FFFFFF` ≈ 15.8:1；次要 `#5A6270` ≈ 6.4:1。
- 深色：正文 `#E8EAED` on `#1E2026` ≈ 13.9:1；次要 `#A8AEBA` ≈ 7.6:1。
- 均超过 WCAG AA 的 4.5:1。`DimTextBrush` 仅用于占位/提示，不承载信息。

**兼容性**：原 17 个 key 全部保留（`WindowBackgroundBrush`…`NavSelectedBrush`），仅调整取值；页面无需改动即可受益。

---

## ADR-3：导航升级（图标 + 文字 + 选中指示条）

**契约保持**：`NavItem { Code, TitleKey, Title }` 不变，仅**新增** `Glyph`。
`MainViewModel.NavItems` / `SelectedNav` / `Navigate(code)` / `GetPage(code)` 的既有行为完全不变，
因此历史窗口回填（`GetRestViewModel`）、`NavigateSearch` 等调用点不受影响。

**图标方案**：使用文本字形（`❤ ⬡ ▦ ☰ ▤ ⚡ ⌘ ◎ ✂ ⚕ ❐`），**不引入任何图标 NuGet 包**。
理由：避免新依赖与打包体积；字形随字体渲染，深浅色主题下自动取前景色。
代价：个别字形在极端字体回退环境下可能显示为方框（装饰性问题，已列入人工核对清单）。

**选中态**：`NavListBoxItemStyle`（Common.xaml）中同时提供
`NavSelectedBrush` 淡底 + 左侧 3px `NavIndicatorBrush` 指示条 + 前景色转 `AccentTextBrush`。

**踩坑记录（重要）**：**必须删除 `TextBlock` 的隐式 `Foreground` 样式**。
一旦给 `TextBlock` 设隐式 Foreground，它会**覆盖从 ListBoxItem 继承的前景色**，导致
① 导航选中态的强调色失效；② 强调色按钮内的文字变深色而非白色。
正文颜色改由 `Window.Foreground` 继承解决。

---

## ADR-4：新页面与功能接线

| 功能 | Core 方法（已实现） | ViewModel | View | 入口 |
|---|---|---|---|---|
| A1 集群指标 | `GetNodeStatsAsync` | `MetricsViewModel` | `MetricsView` | 导航「指标」 |
| A2 Mapping/Settings | `GetMappingAsync`/`PutMappingAsync`/`GetSettingsAsync`/`PutSettingsAsync` | `IndexToolsViewModel` | `IndexToolsWindow` Tab | 索引页「更多操作 → 索引工具」 |
| A3 分词调试 | `AnalyzeTextAsync`/`AnalyzeTextWithBuiltinAsync` | `AnalyzeViewModel` | `AnalyzeView` | 导航「分词」 |
| A4 字段 Top 值 | `FieldTopValuesAsync` | `IndexToolsViewModel` | 同上 Tab | 索引工具 |
| A5 别名管理 | `GetIndexAliasesAsync`/`AddAliasAsync`/`RemoveAliasAsync` | 同上 | 同上 Tab | 索引工具 |
| A6 Reindex 迁移 | `ReindexAsync` | 同上 | 同上 Tab | 索引工具 |
| A7 模板 | `GetIndexTemplatesAsync`/`GetComponentTemplatesAsync`/`Delete*Async` | `TemplatesViewModel` | `TemplatesView` | 导航「模板」 |
| A8 诊断 | `ExplainAllocationAsync`/`HotThreadsAsync`/`GetThreadPoolAsync`/`GetPendingTasksAsync` | `DiagnosticsViewModel` | `DiagnosticsView` | 导航「诊断」 |
| A9 Force Merge | `ForceMergeAsync` | `IndexToolsViewModel` | 同上 Tab | 索引工具 |

**端点路径**（全部经测试断言，且回归"无双斜杠/无漏斜杠"）

| 方法 | 路径 |
|---|---|
| GET | `/_nodes/stats` |
| GET/PUT | `/{index}/_mapping`、`/{index}/_settings` |
| POST | `/{index}/_analyze`、`/_analyze` |
| POST | `/{index}/_search`（Top值聚合：terms + cardinality） |
| GET | `/_alias`、`/{index}/_alias`；POST `/_aliases`（add/remove） |
| POST | `/_reindex` |
| GET | `/_index_template`、`/_component_template`；PUT/DELETE `/_index_template/{name}`；DELETE `/_component_template/{name}` |
| POST | `/_cluster/allocation/explain` |
| GET | `/_nodes/hot_threads`、`/_nodes/{id}/hot_threads`、`/_nodes/thread_pool`、`/_cluster/pending_tasks` |
| POST | `/{index}/_forcemerge?max_num_segments=N` |

**索引名转义**：统一 `IndexPath()`，对逗号分隔的多索引逐个转义并保留逗号（`"a b,c d"` → `a%20b,c%20d`）。

**Analyze 重载命名**：`AnalyzeTextAsync(index, ...)` 与无索引版若同名会产生 `(string,string?)` 调用歧义（**编译期 CS0121**），
故无索引版命名为 `AnalyzeTextWithBuiltinAsync`。

**Mapping/Settings 闭环**：`GET` 返回外层包索引名与 `mappings`/`settings` 包装，而 `PUT` 只接受内层体。
`EsQueryHelper.ExtractMappingBody/ExtractSettingsBody` 在 Core 做提取（可单测），使"读取→编辑→保存"可用。

---

## ADR-5：i18n 策略

**已知坑（本项目真实踩过）**：向字典插入词条时**漏掉行尾逗号**，
会让后一条 `["xxx"] = ...` 被解析成上一条的延续，编译器报出的却是**几百行之外**的
`CS1503: cannot convert from 'string' to 'int'`，报错位置与真实原因完全无关。

**规定做法**
1. 成对写入 zh 与 en，**每条都带行尾逗号**（包括块内最后一条）。
2. 用稳定锚点插入（本轮用 `["common.ok"]` / `["nav.indices"]`），不用行号。
3. 插入后**立刻** build；若出现"位置莫名其妙"的 CS1503，优先怀疑漏逗号。
4. 由守卫机械校验：`zh 与 en 词条数量相等` + `无单边缺失`。

**命名约定**：`metrics.group.*`、`analyze.*`、`diag.*`、`templates.*`、`indextools.*`、`rest.example.*`。

---

## ADR-6：测试与守卫策略

### Core 单测（Linux 可跑，`tests/ElasticDesktopManager.Tests`）
- **端点契约**：用 `FakeHandler` 捕获 `(HttpMethod, AbsoluteUri, Body)`，断言方法/路径/请求体。
- **路径回归（AC3）**：`NoDoubleSlash()` 断言协议后不存在 `//` 与 `/?`。
- **纯函数**：指标扁平化/格式化（11 项）、分词解析、模板解析、Top值解析、别名解析、
  Mapping/Settings 提取、Reindex/别名非法 JSON 统一抛 `EsException`。
- 结果：**99 通过 / 0 失败**。

### 静态守卫（`tests/binding-guard`，10 项）
| 规则 | 防的问题 |
|---|---|
| 只读属性 + 默认 TwoWay 目标 | `TextBox.Text` 等绑 `private set` → **运行期抛异常、编译零错误** |
| zh/en 词条完全对齐 | 单边缺失词条 |
| zh/en 词条数量相等 | 重复 key 掩盖差异 |
| Dark/Light 颜色 key 一致 | 某主题缺 key → 该主题下元素**静默不可见** |
| XAML 无硬编码 `#RRGGBB` | 硬编码色导致切主题失效 |
| 引用的资源 key 均已定义 | `DynamicResource` 缺失→静默不可见；`StaticResource`/`FindResource` 缺失→**抛异常崩溃** |
| **设计令牌类型与目标属性类型匹配** | `Double` 令牌用于 `GridLength`/`Thickness` 属性 → **启动即崩**（缺陷 7 的根因） |
| 资源引用不得嵌在字符串中 | `Margin="0,{StaticResource S},0,0"` → XAML 当字面量 → 运行期转换失败 |
| 守卫自检（2 条） | **假绿**：规则失效却仍显示 PASS |

**关键设计：守卫必须"能失败"**。每条新规则都配自检喂违规样本；本轮还修掉了一个真实误报（见下）。

**类型盲误报的处理**：守卫按属性名全局查找（不解析绑定的 DataContext 类型）。
当同名属性既存在可写声明（`QueryCondition.Value`）又存在只读声明（展示模型 `Value { get; init; }`）时，
会**误报**。修法：采用**保守规则** —— 只有该名字在**所有**声明处都只读才判为只读。
同时把 `public required string X { get; init; }` 这类带修饰符的声明纳入识别。
自检固定三个方向：① 带 `required` 的只读能识别；② 同名可写/只读混合**不误报**；③ 唯一只读属性**仍被抓出**（防止保守规则把守卫改废）。

---

## 风险登记（按严重度）

| # | 风险 | 缓解 |
|---|---|---|
| R1 | **只读属性 TwoWay 绑定**：本轮大改 XAML，这是最易翻车处（运行期异常、编译零错误） | 守卫规则 + 每次改动后跑守卫；只读属性一律加 `Mode=OneWay` |
| R2 | **资源 key 缺失**：XAML `DynamicResource` 缺失静默不可见；C# `FindResource` 缺失直接崩 | 守卫"引用 key 必须已定义"规则（本轮据此发现并修复了 `TextBrush` 缺失导致的崩溃） |
| R3 | **主题 key 不对称**：某主题缺 key → 该主题下元素不可见 | 守卫 Dark/Light key 一致性规则 |
| R4 | 无法在本机运行 GUI，视觉问题不可自测 | 静态守卫 + Core 单测 + **给用户的 Windows 人工核对清单** |
| R5 | 折叠面板默认全展导致上千元素卡顿 | 默认折叠 + 记忆展开状态 + 命中筛选自动展开 |
| R6 | i18n 漏逗号导致误导性编译错误 | ADR-5 的机械做法 + 数量相等守卫 |
| R7 | 子代理不稳定（本轮架构师两次无产出） | 记录降级并由主代理串行承担；流程与门禁不变 |
| R8 | **令牌类型与目标属性不匹配**：编译零错误、本机无法自测、真机启动即崩（**已发生**：`GridLength`） | 令牌按语义声明精确类型（ADR-2 表）+ 守卫「令牌类型匹配」规则 + 对真实文件做负向验证 |
| R9 | 资源 key 规则曾只覆盖 `DynamicResource`，`StaticResource` 缺 key 无规则拦截（**守卫假绿**） | 已补齐 `{StaticResource}` 与 `<StaticResource ResourceKey=.../>`；新增规则必须先证明"能失败" |

## 未完成 / 后续批次（如实声明）

- **A10**：索引数据导出 JSON（带 DSL 过滤）、本地 JSON 批量导入 `_bulk` —— Core 未实现，UI 未接入。
- **B6**：连接管理页的卡片化（悬停抬升 + 状态徽章）—— 未做，现为树形列表（保留文件夹层级）。
- **快照/SLM、ILM、文档完整 CRUD**：属大块功能，列为后续批次。
- 逐条指标中文说明表：见 ADR-1 取舍。
