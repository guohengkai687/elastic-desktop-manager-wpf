# QA.md — 验收执行结果（ES-King 借鉴改造 + 现代精致 UI）

执行环境：Linux（无 Windows 显示，WPF **不可运行**）。
因此可执行的验收 = `dotnet build` + Core 单测 + 静态守卫；勾选类项目另附人工核对清单。

## 执行汇总

| 命令 | 结果 |
|---|---|
| `dotnet build ElasticDesktopManager.sln` | **Build succeeded，0 Warning(s)，0 Error(s)** |
| `dotnet run --project tests/ElasticDesktopManager.Tests -c Release` | **通过 99，失败 0**（基线 48 → 99） |
| `dotnet run --project tests/binding-guard -c Release` | **通过 8，失败 0**（基线 3 → 8） |

## 逐条 AC

| AC | 内容 | 结果 | 证据 |
|---|---|---|---|
| AC1 | 新增端点均有单测（断言 method/path/body） | ✅ | `GetNodeStatsAsync`/`GetMapping`/`PutMapping`/`GetSettings`/`PutSettings`/`AnalyzeText(WithBuiltin)`/`FieldTopValues`/`GetAliases`/`GetIndexAliases`/`AddAlias`/`RemoveAlias`/`Reindex`/`ForceMerge`/模板查询·创建·删除（含组件模板）/`ExplainAllocation`/`HotThreads`(含按节点)/`GetThreadPool`/`GetPendingTasks` 共 20+ 项测试 |
| AC2 | 指标扁平化为 Core 纯函数可测 | ✅ | `EsMetricsFlattener`；11 项测试覆盖：深层嵌套、数组按下标展开、分组顺序稳定、bytes/ms 格式化、按 key 语义自动格式化、布尔/字符串/空串、非法 JSON 返回空、多节点带节点名、空容器标记 |
| AC3 | 新路径无 `//`、无漏 `/` | ✅ | `NoDoubleSlash()` 断言；"全部新增端点路径无漏斜杠/双斜杠" 一项遍历 15 个端点 |
| AC4 | 新增功能均经 i18n，zh/en 严格对齐 | ✅ | 守卫两项 i18n 规则通过；新增约 130 个词条成对写入 |
| AC5 | 控件模板覆盖 hover/focus/disabled 三态 | ✅ | `Themes/Common.xaml` 覆盖 Button(默认/Accent/Danger/Ghost/Icon)、TextBox、PasswordBox、ComboBox(+Item)、CheckBox、RadioButton、ListBoxItem、NavListBoxItem、TreeViewItem、TabItem、DataGrid(+ColumnHeader/Row/Cell)、ScrollBar(+Thumb)、ProgressBar、ToolTip、Expander |
| AC6 | 导航含图标 + 文字 + 左侧强调指示条 | ✅ | `NavItem.Glyph`（11 项）+ `MainWindow` 导航 `DataTemplate`；`NavListBoxItemStyle` 内 `indicator`（3px、`NavIndicatorBrush`）在 `IsSelected` 时显示 |
| AC7 | 新改动 XAML 无硬编码 `#RRGGBB` | ✅ | 守卫"XAML 不存在硬编码颜色"通过（**曾捕获 MainWindow Toast 的 `#CC333333` 并已改为 `OverlayBrush`**） |
| AC8 | zh/en 词条数相等且无单边缺失 | ✅ | 守卫两项通过 |
| AC9 | 无"只读属性 + 默认 TwoWay 目标"绑定 | ✅ | 守卫通过（**过程中修掉 1 处守卫误报**，见下） |
| AC10 | `dotnet build` 0 警告 0 错误 | ✅ | 见汇总 |
| AC11 | Core 单测 ≥48 全通过 | ✅ | 99 通过（只增不减） |
| AC12 | Core 不新增 WPF 依赖 | ✅ | 新增代码全在 `Core/Es`、`Core/Models`，仅用 `System.Text.Json`；`Core.csproj` 无 `UseWPF` |

## 本轮发现并修复的真实缺陷

| # | 缺陷 | 严重度 | 说明与修法 |
|---|---|---|---|
| 1 | `EsExamplesWindow` 调 `FindResource("TextBrush")`，但该 key **不存在**（主题里是 `TextPrimaryBrush`） | **高（崩溃）** | `FindResource` 缺失会抛 `ResourceReferenceKeyNotFoundException`，在筛选框首次获得焦点时**必然崩溃**；编译零错误。改为 `TryFindResource` + 回退的 `Brush()` 助手，并修正 key。新增守卫规则"引用的资源 key 必须已定义"专门防此类问题 |
| 2 | `AnalyzeTextAsync` 两个重载调用歧义 | 中（编译期） | `(string, string?)` 起始签名相同 → `CS0121`。无索引版改名 `AnalyzeTextWithBuiltinAsync`，注释说明原因 |
| 3 | 非法 JSON 泄漏 `JsonException` | 中 | `AddAliasAsync` 的 filter、`ReindexAsync` 的 query 在 `JsonNode.Parse` 抛错时直接冒泡，调用方无法统一按 `EsException` 处理。两处均包 `try/catch (JsonException) → EsException`，并各加回归测试 |
| 4 | `TextBlock` 隐式 `Foreground` 覆盖继承 | 中（视觉） | 会导致导航选中态强调色失效、强调按钮文字变深色。删除该隐式 setter，正文色改由 `Window.Foreground` 继承 |
| 5 | 守卫**误报**：`SearchView` 的 `Value` 绑定被误判 | 中（工具可信度） | 根因：我新增的 `MetricRowVm.Value { get; init; }` 与 `QueryCondition.Value`（可写）同名，而守卫按属性名**类型盲**查找。修法：① 重命名我的属性为 `MetricValue`；② 守卫改**保守规则**（该名字在所有声明处都只读才判只读）；③ 把 `public required string X { get; init; }` 纳入识别；④ 自检固定三方向（带 required 能识别 / 混合不误报 / 唯一只读仍被抓） |
| 6 | Expander 模板 `TargetName="rot"` 编译失败 | 低 | `RenderTransform` 内的命名元素不能作 Trigger 目标（`MC4111`）。改为切换 `Path.Data` |

> 另有一处**测试断言自身写错**：曾断言 `_nodes/stats` 每节点 1 行，实际 2 行（`name` + 指标）。
> 核对后确认**产品行为正确**（把节点名也作为一行展示有用），改的是断言而非代码。

## 第 2 轮：交付后用户实测反馈

用户在 Windows 上实运行时**启动即崩溃**（`System.Windows.Markup.XamlParseException`），
由此暴露出一类此前**编译 + Core 单测 + 守卫全都覆盖不到**的缺陷。

| # | 缺陷 | 严重度 | 说明与修法 |
|---|---|---|---|
| 7 | **启动即崩**：`MainWindow.xaml` 行 11，`"52"不是属性"Height"的有效值` | **致命（无法启动）** | 根因：`Tokens.xaml` 把 `TitleBarHeight`/`StatusBarHeight`/`NavWidth` 声明为 `sys:Double`，而 `RowDefinition.Height` / `ColumnDefinition.Width` 的类型是 `GridLength`，其 `GridLengthConverter` **只接受字符串、不接受数字**。修法：令牌按语义声明为**精确类型**（间距=`Thickness`、网格尺寸=`GridLength`、字号/控件高=`Double`），使用点不再需要任何类型转换 |
| 8 | 同族**未爆发**缺陷：`CardStyle` 用 `sys:Double` 令牌设 `Padding`（目标 `Thickness`） | 高（一旦生效必崩） | 会随页面渲染立刻崩。随 #7 一并修复（`Space*` 改为 `Thickness`） |
| 9 | 守卫**真实漏洞**：资源 key 规则只查 `{DynamicResource}`，**从未检查 `{StaticResource}`** | 高（守卫假绿） | `{StaticResource}` 缺 key 同样抛 `XamlParseException` 直接崩，却无规则覆盖。已补齐 `{StaticResource X}` 与 `<StaticResource ResourceKey="X"/>` 两种写法，并顺带拦截"资源引用被嵌在字符串中"（`Margin="0,{StaticResource Space2},0,0"` 会被 XAML 当字面量 → 运行期转换失败） |

**新增守卫规则**：「设计令牌的声明类型与目标属性类型匹配」（内置属性→类型表，可解析 `Setter` 的 `TargetType`）。

**负向验证（证明规则真能失败，不是假绿）**：

1. 把 `TitleBarHeight` 改回 `sys:Double` → 守卫精准报出
   `MainWindow.xaml: <RowDefinition Height="{StaticResource TitleBarHeight}"> → 令牌声明为 Double，该属性需要 GridLength`；还原后通过。
2. 在真实视图里注入 `Style="{StaticResource CardStyleTypo}"` + `Padding="0,{StaticResource Space4},0,0"` → 两条都被报出；还原后 0 行差异。

守卫 **8 → 10 项**（自检 3 → 5 组断言）。

**另外三项静态审计**（确认无同类残留）：

1. `Tokens`/`Common`/`Light`/`Dark` 四个字典在 `App.xaml` 的同一次合并中**无重复 key**（重复 key 会启动即抛）。
2. 15 个 `Style` 的 `TargetType` 与实际使用元素**全部相符**（不符会在加载时抛异常）。
3. 各样式 `Setter` 的属性逐条核对，均在对应 `TargetType` 上存在。

## Core 单测明细（99 项，分组）

| 组 | 数量级 | 覆盖 |
|---|---|---|
| URL 规整 / 存储 / JSON / i18n / EsQueryHelper（既有） | ~40 | 基线回归 |
| 查询示例目录（上轮新增） | 9 | 目录完整性、body JSON 合法（`_bulk` 按 NDJSON 逐行）、中英词条齐备、占位符替换与边界、TitleKey 唯一 |
| 新增端点契约 | ~16 | method/path/body、索引名转义、多索引逗号、注入路径回归 |
| 指标扁平化 | 11 | 见 AC2 |
| 分词解析 | 3 | 英文/中文 token、无 tokens 字段 |
| 模板解析 | 3 | index_patterns/priority/version/排序、composed_of、空对象 |
| 字段 Top 值解析 | 4 | buckets+cardinality、数值 key、ES error 保留原因、无 aggregations |
| 别名解析 | 4 | 名称/索引/filter/routing、routing 回退、无 aliases、同名跨索引 |
| Mapping/Settings 提取 | 3 | GET→PUT 体提取、已是目标形态原样返回、非法 JSON 透传 |
| 非法 JSON 统一异常 | 2 | 别名 filter、Reindex query |

## Windows 人工核对清单（本机无法自测，请复验）

> 命令：`dotnet run --project src/ElasticDesktopManager`
>
> **⚠️ 第 2 轮修复后请优先确认：主窗口能正常打开**（此前会抛 `XamlParseException` 启动即崩）。
> 若仍打不开，请把**完整异常文本**发我——`XamlParseException` 会给出文件名、行号与属性名，可一次定位。

1. **深色/浅色主题**：点顶栏主题按钮切换，检查所有页面文字/表格/输入框**无不可见元素**（若某处"空白"，多半是主题缺 key）。
2. **导航**：左侧 11 项应显示图标 + 文字；选中项有**左侧强调竖条** + 淡底 + 强调色文字；悬停有底色变化。
3. **指标页**：进入后应自动加载；分组默认可折叠（首组展开）；筛选框输入 `heap` 应只剩含 heap 的行且分组自动展开。
4. **分词页**：填文本 + 分词器（如 `standard`），点分词，表格应出现 token/类型/位置/偏移。
5. **诊断页**：四个页签；分片分配解释留空点执行应返回 ES 的解释 JSON；线程池/挂起任务进入页面即加载。
6. **模板页**：索引模板/组件模板两个页签，选中左侧行右侧应显示 JSON；删除有二次确认。
7. **索引工具**：索引页每行「更多操作 → 索引工具」；Mapping/Settings 可"重新加载→编辑→保存"并回读；字段 Top 值查询；Force Merge 与 Reindex 有确认框。
8. **REST 页**：请求体面板高度足够编辑多行 JSON；「ES 查询示例」筛选与应用正常。
9. **Toast**：执行保存/删除后底部提示应可见（深色下浅色浮层、浅色下深色浮层）且 3 秒后淡出。
10. **图标字形**：确认导航图标不是方框（若个别是方框，属字体回退，可换成更常见字形）。

## 未执行项（如实声明）

- 未做 GUI 截图比对（本机无 Windows）。
- 未做真实 ES 集群联调（无可用集群）；端点正确性靠请求断言（method/path/body）+ 解析单测保证，**返回结构的健壮性**（如不同 ES 版本字段差异）未经真机验证。
- A10（导出/导入）与 B6（连接页卡片化）未实现，见 ARCHITECTURE.md「未完成」。


## 第 3 轮：功能增删 + UI 重新设计（用户 5 项要求）

### 逐项结论（含"已存在"的如实说明）

| # | 要求 | 结论 |
|---|---|---|
| 1 | 去除分词/诊断/模板三个功能 | 已完全移除：9 个视图/VM 文件、导航项、i18n 词条（74 行）、Core 端点与解析器/模型（含其 12 个测试），**未留死代码**；历史实现可从 git 历史取回 |
| 2 | 增加快照管理 | 新增 `_snapshot` 全套能力：仓库（列出/新建/校验/删除）+ 快照（列出/创建/删除/恢复）+ 进行中状态查询；创建与恢复用 `wait_for_completion=false` 避免挂住 UI；仓库名/快照名做 URL 转义（有回归测试） |
| 3 | 搜索页自动加载索引 + 下拉选择 | **此功能上一轮已实现**（`LoadIndicesAsync` + ComboBox）。本轮修正两个真实缺口：① 下拉改为**可编辑**，之前只能选具体索引、无法输入 `logs-*` 通配符/别名/多索引；② 加载失败原先被**静默吞掉**（下拉为空且无任何提示），现在把数量或失败原因显示在下拉右侧 |
| 4 | 连接按钮不可点击 | **真实缺陷，已修**：`ConnectionsViewModel.Selected` 的 setter 只通知了 `HasSelection`，漏了 `CanConnect` → 「连接」「测试」按钮永久停在初始 `IsEnabled=false`（编辑/删除却正常，与用户观察一致）；双击能连是因为双击直接 `Execute` 命令、绕过了 `IsEnabled` |
| 5 | UI 还是之前的样子 | **真实缺陷 + 重新设计**。缺陷：`ThemeService` 用 `dicts.RemoveAt(1)` 删旧主题词典并注释假定"索引 0 = Common.xaml"，而上一轮在 Common 前插入了 Tokens.xaml，索引整体后移 → 切主题时被删掉的是 **Common.xaml（整套控件模板）**，控件回退成 WPF 默认外观。重新设计见下 |

### 第 5 项的缺陷根因（值得单独记一笔）

```
App.xaml 合并顺序： [0]=Tokens  [1]=Common  [2]=Light
ThemeService 旧代码：dicts.Add(next); dicts.RemoveAt(1); // 注释："索引 0 = Common.xaml"
```

启动时 `Settings.Theme` 默认 `light`、`Current` 也是 `light` → 早退，所以**启动那一瞬间样式是好的**；
一旦点顶栏主题按钮（或系统偏好变化），删掉的就是 Common.xaml：所有隐式样式消失、控件变成 WPF 默认灰白外观。
修法：**按 `ResourceDictionary.Source` 识别主题词典**，不再依赖任何下标；并新增守卫规则禁止字面量下标。

### 本轮新增守卫规则（10 → 13 项）与负向验证

| 新规则 | 防的问题 | 负向验证 |
|---|---|---|
| 被 XAML 绑定的只读派生属性必须有 PropertyChanged 通知 | 派生属性漏通知 → 按钮永久禁用（本次的连接按钮 bug） | 撤掉 `CanConnect` 的通知后，守卫精准报出 `CanConnect（依赖可变的 Selected）`；还原后通过 |
| 不得用字面量下标增删 MergedDictionaries | 合并顺序一变就拆掉样式/资源 | 自检喂 `MergedDictionaries.RemoveAt(1)` 必须被抓出；`Count - 1` 不误报 |
| 令牌类型规则扩展到 Static+Dynamic 与主题词典，并校验两主题同 key 同类型 | 主题风格令牌写错类型（同样是运行期崩溃）；Light/Dark 同名令牌类型不一致（一个主题正常、另一个崩） | 自检样本构造 Light=Double / Dark=Thickness 必须判定为冲突 |

`init`-only 属性**不算**可变（构造后永不改变），否则 `NavItem.Item` / `ConnectionGroupKey` 一类只读派生属性会误报 —— 自检已固定这一方向。
规则上线后对现有全部 VM 跑了一遍，除连接按钮外**未发现其它同类缺陷**。

### 三项静态审计（确认无残留）

1. `Tokens`/`Common`/`Light`/`Dark` 在一次合并中无重复 key（重复 key 会启动即抛）。
2. 全部 `Style` 的 `TargetType` 与实际使用元素相符。
3. 各样式 `Setter` 属性逐条核对，均在对应 `TargetType` 上存在。

### 本轮验证

- 构建：**0 警告 0 错误**
- Core 单测：**96/96**（移除 12 项已删功能的测试，新增 9 项：快照端点/解析 6、图标校验 3）
- 静态守卫：**13/13**

### 追加到 Windows 人工核对清单

11. **切主题后样式不能丢**：点顶栏主题按钮切到深色再切回浅色，按钮/表格/滚动条应**始终是圆角自定义外观**；
    若出现 WPF 默认灰白控件，说明 Common.xaml 又被移除了（本轮已修，此条用于确认）。
12. **导航图标**：应为**线性矢量图标**（房子/服务器/网格/数据库/柱状图/放大镜/相机/终端/表格），
    不是 ❤ ⬡ ✂ 这类符号，也不应是方框（矢量路径不依赖字体，方框问题不应再出现）。
13. **两套主题的性格差异**：浅色应明显更"松"（大圆角、更高控件、卡片有柔和阴影）；
    深色应明显更"紧"（小圆角、更矮控件、几乎无阴影、描边更暗）。
14. **快照页**：仓库下拉能列出仓库；「新建仓库」需 ES 已在 `elasticsearch.yml` 配置 `path.repo`，
    否则会返回明确的 ES 错误（页面会把它显示出来，不再静默）。
