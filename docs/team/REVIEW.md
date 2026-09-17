# REVIEW.md — 审查意见与处置（ES-King 借鉴改造 + 现代精致 UI）

审查者：主代理自查 + 静态守卫（子代理审查不可用，见下"流程说明"）。
审查轴：**标准轴**（本仓库约定：分层、i18n、守卫、零警告）+ **规格轴**（SPEC.md 的 AC1–AC12）。

## 流程说明（如实记录）

本轮按 `dsh-skill-team-omo` 的 **standard** 模式启动，
但**架构师子代理两次启动后均以无产出失败告终**（第一次未写任何文件即失败；按协议 `send_message` 追问一次后仍失败）。
依据 skill 的降级预案（"子代理已结算但无有效产出"属可验证故障），
停止该子代理并**由主代理串行承担架构师角色**（产出 `ARCHITECTURE.md`），
同时**由主代理自查承担审查**。流程与门禁未跳过，但**审查独立性弱于前一轮**（前一轮有独立审查子代理）。

---

## P0（阻断级）

**无遗留 P0。**

过程中出现并已修复的两个"运行期崩溃、编译零错误"级别问题，按严重度应记 P0，均已闭环：

| ID | 问题 | 处置 | 验证 |
|---|---|---|---|
| P0-1 | `EsExamplesWindow` 使用 `FindResource("TextBrush")`，该 key **不存在** → 筛选框首次聚焦时抛 `ResourceReferenceKeyNotFoundException` 崩溃 | 改为 `TryFindResource` + 回退的 `Brush()` 助手，并修正为 `TextPrimaryBrush`。**并补守卫规则**"引用的资源 key 必须已定义"（同时覆盖 XAML `DynamicResource` 与 C# `FindResource`） | 守卫 8/8；规则含自检 |
| P0-2 | `TextBlock` 隐式 `Foreground` 覆盖继承 → 导航选中态强调色失效、强调按钮文字深色（可用性缺陷） | 删除该隐式 setter；正文色由 `Window.Foreground` 继承 | build 0/0；已在 ADR-3 记录踩坑 |

## P1（重要）

| ID | 问题 | 处置 |
|---|---|---|
| P1-1 | 守卫**误报**（`SearchView` 的 `Value` 被误判只读）——假绿/假红都会摧毁守卫可信度 | ① 新增属性重命名 `MetricValue` 消除同名冲突；② 守卫改保守规则（全部声明只读才判只读）；③ 识别 `public required ... { get; init; }`；④ 自检固定三方向（含"唯一只读仍被抓出"防止把守卫改废）。见 ARCHITECTURE.md ADR-6 |
| P1-2 | `AnalyzeTextAsync` 重载歧义（`CS0121`） | 无索引版改名 `AnalyzeTextWithBuiltinAsync`，XML 注释说明原因 |
| P1-3 | 非法 JSON 泄漏 `JsonException`（`AddAliasAsync`/`ReindexAsync`） | 两处包 `try/catch (JsonException) → EsException`，各加回归测试 |
| P1-4 | 指标分组若默认全展开，多节点下会实例化上千可视元素 | 默认折叠 + 记忆展开状态 + 筛选命中自动展开；并在 ADR-1 记录 |

## P2（次要，已处置或明确接受）

| ID | 问题 | 处置 |
|---|---|---|
| P2-1 | Expander 模板 `TargetName="rot"` 编译失败（`MC4111`） | 改为切换 `Path.Data` |
| P2-2 | 未为每条 ES 指标 key 提供中文说明 | **明确接受**（ADR-1 取舍）：避免半中半英的割裂与双份维护；改以分组本地化 + 原始 key + 可读值 + 节点名 |
| P2-3 | 导航图标为文本字形，极端字体回退下可能显示方框 | 接受；已列入用户人工核对清单第 10 项 |
| P2-4 | `RebuildTree` 中先建 `ExampleNode` 列表再转 `TreeItem` 属冗余 | 已内联到 `TreeItem`，删除中间结构 |
| P2-5 | `PageViewModelBase.ReloadAsync` 对 `IndexToolsViewModel` 语义是"加载全部页签" | 接受，注释已说明 |

## 标准轴核对

| 项 | 结果 |
|---|---|
| Core 不含 UI 类型（`System.Windows.*`） | ✅ 新增代码仅用 `System.Text.Json` |
| 颜色走 `DynamicResource`、几何走 `StaticResource` | ✅ 守卫"无硬编码颜色"通过 |
| 端点方法遵循既有 `ExecuteAsync(method,path,body,timeout,ct)` 约定 | ✅ 全部在 `EsClient.Operations.cs` 的 partial 扩展中 |
| 新增 UI 文案全部经 `Localization.L` | ✅ 无硬编码中文/英文（除示例数据与符号） |
| 只读属性绑定均显式 `Mode=OneWay` | ✅ 守卫通过 |
| 构建零警告零错误 | ✅ |
| 注释说明"为什么"而非"是什么" | ✅ 关键决策（重载改名、隐式样式删除、折叠默认值、逗号陷阱）均有理由注释 |

## 规格轴核对

逐条见 `QA.md` 的 AC1–AC12 表，**全部满足**。未实现项（A10 导出/导入、B6 连接页卡片化）已在
`ARCHITECTURE.md` 与 `QA.md` 明确声明，不掩饰为"已完成"。

## 结论

- **无未处置 blocker**。
- 本轮交付：Core 能力层（A1–A9 端点 + 3 个纯函数工具）、4 个新页面 + 1 个索引工具对话框、
  完整设计令牌与双主题色板、全套控件三态模板、图标导航、精修壳层。
- 遗留（如实）：A10、B6、快照/SLM/ILM/文档 CRUD、逐条指标说明表、真机 GUI 与集群联调。
- **审查独立性提示**：本轮审查由主代理自查完成（子代理不可用），
  下一轮如有条件，建议由独立审查子代理复核 `Themes/Common.xaml` 的模板三态完备性与新页面 XAML。


---

# 第 4 轮审查（提交 8d41960 / 657cbbf / c5d0151）

审查者：**独立子代理**（只读，不改文件，anti-pattern 式对抗排查）+ 主代理自查。
范围：深色背景 / 索引下拉 / 快照五列表三项修复，以及本轮新增的守卫规则。

## 审查意见的核实（4 条成立、1 条不成立）

| # | 审查意见 | 核实结果 | 处置 |
|---|---|---|---|
| B1 | 重写 `SnapshotView.xaml.cs` 时丢了父提交里的 `Localization.LanguageChanged += ApplyTexts`，页面被 MainViewModel 缓存 → 切语言时整页 chrome 停旧语言、VM 状态行已切新语言（中英混排） | **成立**（`git show 8d41960^:.../SnapshotView.xaml.cs` 确认原文件第 15 行有此订阅） | 已恢复订阅；并把 VM 的语言切换改为 `RelocalizeStatuses()` |
| B2 | 五个页签共用 `IsFormOpen`/`ToggleFormCommand`：在"仓库"页签点新建会把另外三个表单一起展开 | **成立** | 拆成 4 个 bool + 带 `CommandParameter` 的 `ToggleFormCommand`；XAML 8 处绑定同步更新 |
| B3 | `ComboTemplateHasEditableBox` 不可靠：`Contains` 一路搜到文件尾、只认字面量 `IsEditable="True"` | **成立** | 重写为 `XDocument` 解析：限定 ComboBox 模板自己的**名字域**（排除嵌套 ToggleButton 模板）、注释天然不算、`IsEditable` 识别放宽并排除显式 False；自检新增 3 个方向（注释/子模板/后面的模板） |
| B4 | `LoadDictionary` 把 Zh+En 合成一个 key 集合，而"唯一跨词典规则只比数量" → 等量但不同 key 的词典会全过 | **不成立** | 守卫第 171 行 `i18n：zh_CN 与 en 词条完全对齐（无单边缺失）` 已经在做 `zh.Except(en)` / `en.Except(zh)` 集合差比对（`Program.cs:185-188`）。审查者漏看了这条已有规则 |
| B5 | 宽松遍的"首段必须命中已有前缀"闸门会静默放过首段拼错的 key | **成立** | 去掉前缀闸门，改为显式白名单 `NotI18nLiterals()`（当前为空，说明 WPF 工程在更严规则下依然干净）；自检新增"首段拼错必须抓出"与"白名单不得用来掩盖真实词条"两条 |

**关于 B4 的教训**：审查意见同样要核实，不能照单全收。5 条里混着 1 条不成立，
如果直接按它去改守卫，反而会把一条已经正确的规则改坏。

## 已处置的次要意见

| 意见 | 处置 |
|---|---|
| Restore/Slm/Ilm 三条状态行不随语言切换 | 已修：抽出 `Update*Status()`，语言切换时 `RelocalizeStatuses()` 重算五条 |
| `657cbbf` 去掉"错误态提前返回"后，语言切换会把 ES 错误文案覆盖成成功文案 | 已修：语言切换只重写**成功文案**，`IsError` 为真时保留 ES 原文（原文不需要翻译） |
| `HostOf`：`type=SNAPSHOT` 的 `source` 没有 host/name → 恢复页"来源"列在主场景恒空 | 已修：回退到 `repository/snapshot`；补测试（第 3 个分片样本） |
| `ParseRecovery` 缺 `ValueKind` 判断，根成员非对象时会抛 | 已修：补 `prop.Value.ValueKind != JsonValueKind.Object → continue` |
| `RetentionText`/`Indices` 解析了却没有列显示；`PhaseCount`/`IsFailed`/`IsDone` 无人使用 | 已修：SLM 表格新增"保留"列（并收紧列宽）；恢复状态圆点改用 `IsDone`；删除 `PhaseCount`/`IsFailed`（本仓库不留死代码） |
| 没有守卫规则保证新增窗口会套 `WindowBaseStyle` | 已修：新增第 18 项规则 + 自检两方向 + 对真实文件负向验证 |
| `SelectedRepository` setter 的两处 fire-and-forget：`ReplaceAll` 会让 DataGrid 把 SelectedItem 推回 null → 每次刷新多一轮重复 GET 且互相竞争 | 已修：`_suppressSelectionReload` 标记 + 由 `LoadRepositoriesAsync` 统一收尾一次加载 |
| `ApplyHeaders` 静默吞掉越界，且 XAML 无英文占位表头（与注释矛盾） | 部分接受：注释已改为"XAML 不放文案，全部由 code-behind 赋值"；越界仍选择静默跳过（对最终用户而言空表头优于崩溃），列数对齐由主代理脚本核对（3/7/9/8/5 与数组一一对应）。**遗留**：守卫尚未覆盖"列数 ↔ 表头数组"的一致性 |
| 解析时格式化的文本（保留/统计/分片）缓存在模型上，切语言不会重解析 | **接受**：要彻底解决得把原始数值也放进模型并逐行格式化，代价与收益不成比例。缓解：下次刷新即恢复当前语言 |

## 明确无法在本机验证（WPF 跑不了）

- 新 ComboBox 模板运行期能否真正进入编辑态、z-order/命中测试是否符合预期（静态看路径正确：非编辑态 `content` 可见、输入框收起；编辑态相反，且输入框右侧留 28px 不遮挡箭头）。
- **"缺 `PART_EditableTextBox`" 是否就是"下拉列表没有数据"的根因**：审查者对照 dotnet/wpf 的 ComboBox 实现后认为它解释不了"列表为空"——这一条我接受，见下。
- 删除 `HasItems=False → 强制关 Popup` 是否确有必要（按阅读只影响"列表为空时点不开"这一情形，删掉更安全）。
- ES 真实响应形状（`/_recovery` 的 `format` 参数、`/_ilm/policy` 是否带 `in_use_by`、SLM `next_execution_millis`）——本机无 ES，只有手写 fixture。

## 结论

- 深色背景：**成立**。诊断（隐式 `TargetType="Window"` 样式不作用于派生窗口）与修法（11 个窗口显式套样式 + MainWindow 直写 Background）都已在代码里闭环，并新增守卫防回归。
- 索引下拉：**部分成立，且根因未完全证实**。缺 `PART_EditableTextBox` 是真实的模板契约违规（可编辑区确实坏），删除有副作用的触发器也合理，但**它解释不了"列表为空"** —— 加载/解析路径与旧代码功能等价。因此本轮的实际交付是"修好可编辑能力 + 让失败可见（数量/空态/错误原因 + 手动刷新按钮）"，而不是"已定位根因"。这一点已如实告知用户，并请其在 Windows 上复验。
- 快照五列表：**成立**（B2 修掉后）。五个列表、按页签懒加载、`TabStatus` 独立报错都已具备；保留列已可见；恢复页"来源"列已能显示。
