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

### 静态守卫（`tests/binding-guard`，17 项）
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
| **被 XAML 绑定的只读派生属性必须有 PropertyChanged 通知** | 漏通知 → 按钮永久禁用（第 3 轮的真实缺陷） |
| **不得用字面量下标增删 MergedDictionaries** | 合并顺序一变就拆掉控件模板（第 3 轮"UI 变回旧样子"的根因） |
| **可编辑 ComboBox 模板必须含 `PART_EditableTextBox`** | 模板缺部件 → WPF 进不了编辑态，下拉不可用（第 4 轮的真实缺陷） |
| **代码里用到的 i18n key 必须存在** | 漏词条 → 界面直接显示 `common.save` 这种 key（第 4 轮发现 2 处历史遗留） |
| **zh/en 同一条词条的占位符必须一致** | 漏占位符 → 英文界面静默丢参数；多占位符 → `FormatException` 被吞后直接显示带 `{}` 的格式串 |
| 守卫自检（5 条） | **假绿**：规则失效却仍显示 PASS |

**关键设计：守卫必须"能失败"**。每条新规则都配自检喂违规样本；本轮还修掉了一个真实误报（见下）。

**类型盲误报的处理**：守卫按属性名全局查找（不解析绑定的 DataContext 类型）。
当同名属性既存在可写声明（`QueryCondition.Value`）又存在只读声明（展示模型 `Value { get; init; }`）时，
会**误报**。修法：采用**保守规则** —— 只有该名字在**所有**声明处都只读才判为只读。
同时把 `public required string X { get; init; }` 这类带修饰符的声明纳入识别。
自检固定三个方向：① 带 `required` 的只读能识别；② 同名可写/只读混合**不误报**；③ 唯一只读属性**仍被抓出**（防止保守规则把守卫改废）。

---


## ADR-7：矢量图标系统（不用字体字形）

**决策**：导航与顶栏图标一律用 24×24 描边式矢量路径，不用字体字形。

**背景**：首版用 `❤ ⬡ ▦ ☰ ▤ ⚡ ⌘ ◎ ✂ ⚕ ❐` 这类 Unicode 符号当图标。用户截图复核后确认这是
"界面看起来不专业"的最大来源：语义不一致（首页=爱心、分词=剪刀、诊断=医疗杖）、笔画粗细不一，
且字体字形在缺字体回退时可能显示成方框。

**做法**：
- 路径数据放 Core（`Core/Ui/AppIcons.cs`，纯字符串、无 WPF 依赖）→ **能在 Linux 上测试**；
- WPF 侧 `Services/AppIconGeometries.cs` 用 `Geometry.Parse` 解析并静态缓存；
- 只用 `M/L/H/V/C/A/Z` 绝对命令；坐标全部落在 0..24；整圆用两段半圆弧；
- `Stretch="None"` + 固定 24×24（**不能**用 `Uniform`：V 形箭头会被按包围盒拉成方块）；
  小尺寸用 `LayoutTransform` 等比缩放（并补回描边粗细），保证不变形；
- 描边色绑定到 `ListBoxItem.Foreground`，选中态自动变强调色。

**验证**：Core 测试逐条校验语法/命令元数/坐标范围/整圆弧成对，并断言"每个 const 都已登记进 All"
（防止新增图标漏登记）。开发期还用 Python 把路径渲染成 PNG 做了一次肉眼复核（WPF 在 Linux 不可运行）。

---

## ADR-8：主题性格令牌（深浅两套不止换色）

**决策**：把"控件圆角 / 控件高度 / 卡片圆角 / 卡片内边距 / 阴影"从 Tokens.xaml 移到 Light.xaml、Dark.xaml，
统一用 `DynamicResource` 引用。

**理由**：用户明确要求 浅色=现代精致、深色=专业工具。这两者是**形状与密度**的差异，不是颜色差异：
- 浅色：圆角 8/12、控件高 32、卡片内边距 18、柔和多层阴影；
- 深色：圆角 4/6、控件高 28、卡片内边距 12、几乎无投影、更低对比描边。

如果这些值留在 Tokens.xaml，只能用 `StaticResource`（解析一次），切换主题不会生效 —— 这是令牌系统的一个结构性陷阱。

**代价/约束**：
1. 这些 key 必须**两套主题都有**（守卫强制 key 一致，并额外校验**同 key 同类型**）；
2. 引用处必须是 `DynamicResource`，`StaticResource` 在主题词典合并前就解析、取不到值；
3. 合并字典出现同名 key 属于**有意覆盖**（主题覆盖令牌层），与"同一字典内重复 key"不同（后者会抛异常）。

---

## ADR-9：窗口背景必须由显式样式提供（隐式样式不作用于派生窗口）

**决策**：把原来的隐式 `<Style TargetType="Window">` 改成带 key 的 `WindowBaseStyle`，每个窗口根元素显式
`Style="{StaticResource WindowBaseStyle}"`；`MainWindow` 另外再直写一次 `Background`（双保险）。

**背景（真实缺陷）**：深色主题下整个页面区是白底、文字几乎不可读。根因不是色板写错，而是
**WPF 的隐式样式按控件具体类型查资源**：`TargetType="Window"` 的隐式样式不会作用到
`MainWindow : Window` / `SettingsWindow : Window` 这些派生类（[dotnet/wpf#10461](https://github.com/dotnet/wpf/issues/10461)），
于是客户区一直停在默认的 `SystemColors.WindowBrush`（系统浅色下正好是白色）。
浅色主题下"白底"恰好正确，所以这个缺陷能一直藏着 —— **只有在深色主题下才暴露**。

**为什么难发现**：导航栏/顶栏/状态栏/卡片都各自设了 `SurfaceBrush`，只有页面容器是透明的，
所以"除页面底色外全是深色"这种半对半错的样子极容易被误判成"色板配错"。

**测试**：本机跑不了 WPF，无法用运行时断言；改成"每个窗口显式引用样式"这一可静态检查的形态，
并由 `Window` 家族（11 个窗口）统一遵守。

---

## ADR-10：展示文本的本地化边界（Core 解析器可以直接用 Localization）

**决策**：需要"拼成一句话"的展示文本（分片统计、保留策略、SLM 统计）在 Core 解析器里用
`Localization.L(...)` 组装，其余文案仍由视图/VM 层负责。

**背景**：`DataGrid` 是按行绑定**模型属性**的，VM 无法逐行格式化；若在 Core 里写死英文，
中文界面就会出现 "2/2 · 1 failed" 这类半截英文（历史遗留）。`Localization` 本来就位于 Core，
调用它不违反"Core 不依赖 UI"的约束。

**代价**：解析结果的语言随当前语言设置变化 → 单测断言只针对数值片段（如 `Contains(text, "30d")`），
不做整句字面量比对，避免语言相关断言。

**边界**：路径、字段名、`_cat` 返回的原始字段（如 `node.role`）不翻译，它们本就是 ES 的术语。

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
| R10 | **派生属性漏发通知**：界面永久停在旧值（第 3 轮连接按钮的真实缺陷），编译与运行均不报错 | 守卫「只读派生属性必须有 PropertyChanged」规则 + 负向验证；所有依赖 Selected 一类可变状态的派生属性都在 setter 里显式通知 |
| R11 | **按固定下标操作资源字典**：合并顺序一变就拆掉样式（第 3 轮"UI 变回旧样子"） | ThemeService 改为按 Source 识别；守卫禁止 `MergedDictionaries` 字面量下标 |
| R12 | 矢量图标路径写错 → 运行期 `Geometry.Parse` 抛异常（本机无法验证渲染） | 图标数据放 Core + 测试逐条校验语法/坐标；开发期用 Python 渲染 PNG 做肉眼复核 |
| R13 | **窗口用隐式样式**：`TargetType="Window"` 不作用于派生窗口 → 客户区停在系统白底（深色主题下刺眼；**已发生**） | ADR-9：`WindowBaseStyle` 显式引用；MainWindow 直写 `Background` |
| R14 | **可编辑 ComboBox 缺模板部件**：`IsEditable="True"` 但模板无 `PART_EditableTextBox` → 下拉不可用（**已发生**） | 守卫「可编辑 ComboBox 模板」规则；唯一可编辑下拉单点覆盖 |
| R15 | **i18n key 漏定义**：界面直接显示 `common.save` 这类 key（**已发生 2 处**） | 守卫「代码里的 key 必须存在」规则（调用点精确遍 + WPF 工程字面量宽松遍） |

## 未完成 / 后续批次（如实声明）

- **A10**：索引数据导出 JSON（带 DSL 过滤）、本地 JSON 批量导入 `_bulk` —— Core 未实现，UI 未接入。
- **B6**：连接管理页的卡片化（悬停抬升 + 状态徽章）—— 未做，现为树形列表（保留文件夹层级）。
- ~~**快照/SLM、ILM**~~：第 4 轮已实现（快照页五个列表，见 ADR-10 与 ARCHITECTURE 的 A10b/A10c）。
- **ILM 的 start/stop 与运行状态**：`_ilm/start|_ilm/stop|_ilm/status` 未接入 UI（本轮只做"五个列表"范围内的策略 CRUD）。
  为避免留死代码，对应客户端方法在交付前**已删除**；需要时再加。
- **快照仓库的 S3/GCS/Azure 校验**：类型下拉已支持，但设置项只提供 location/compress（其余需手写 JSON）。
- **文档（documents）完整 CRUD**：仍为后续批次。
- 逐条指标中文说明表：见 ADR-1 取舍。
