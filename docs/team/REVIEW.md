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

# 第 4 轮审查（提交 835e3a1 / 666f096 / ed9ebce）

审查者：**独立子代理**（只读，不改文件，anti-pattern 式对抗排查）+ 主代理自查。
范围：深色背景 / 索引下拉 / 快照五列表三项修复，以及本轮新增的守卫规则。

## 审查意见的核实（4 条成立、1 条不成立）

| # | 审查意见 | 核实结果 | 处置 |
|---|---|---|---|
| B1 | 重写 `SnapshotView.xaml.cs` 时丢了父提交里的 `Localization.LanguageChanged += ApplyTexts`，页面被 MainViewModel 缓存 → 切语言时整页 chrome 停旧语言、VM 状态行已切新语言（中英混排） | **成立**（`git show 835e3a1^:.../SnapshotView.xaml.cs` 确认原文件第 15 行有此订阅） | 已恢复订阅；并把 VM 的语言切换改为 `RelocalizeStatuses()` |
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
| `666f096` 去掉"错误态提前返回"后，语言切换会把 ES 错误文案覆盖成成功文案 | 已修：语言切换只重写**成功文案**，`IsError` 为真时保留 ES 原文（原文不需要翻译） |
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

---

# 第 5 轮审查记录

范围：用户截图缺陷（搜索页条件行两个下拉）+ 一份独立 Core/ES 复审报告。
复审报告写于 `835e3a1`，而当时 HEAD 已是 `7a0b135` —— 很多意见**在报告写出前就已被 `7a0b135` 修掉**。
按纪律逐条回代码核实，**不以报告的"Blocking"标签为准**。

## 复审意见逐条核实（7 条成立、1 条不成立、10 条已在 `7a0b135` 修掉）

### 成立且本轮已修

| # | 意见 | 核实结果 | 处置 |
|---|---|---|---|
| C1 | ILM「修改时间」列恒为裸毫秒：`FormatIsoDate(GetString(el,"modified_date"))` | **成立**（`LifecyclePolicyMetadata.java` 7.17 / 8.17 / main 均为 `declareLong`，ISO 在 `modified_date_string`；`JsonHelper.GetString` 对 Number 走 `GetRawText()`，`FormatIsoDate` 失败后原样返回） | 改 `TimestampOf(el, "modified_date", "modified_date_string")`；fixture 改成毫秒真实形状 + 正向断言格式化结果 |
| C2 | ILM 阶段链顺序不可信（`Collectors.toMap` → HashMap） | **成立**（并进一步确认 `readImmutableMap` 路径下顺序甚至随 JVM 随机） | 按 `ORDERED_VALID_PHASES` 稳定排序；测试用打乱顺序的 fixture 断言输出顺序 |
| C3 | `ParseRunInfo` 的 ISO 伴随字段名应为 `time_string`（写的是 `time_millis`，永不触发） | **成立**（`SnapshotInvocationRecord.toXContent` 写 `time` + `time_string`；行为当前正确，属死分支） | 改为 `time_string`；新增"只有 `time_string`"的用例 |
| C4 | `ApplyHeaders` 越界静默跳过，且守卫不覆盖"列数 ↔ 表头数组" | **成立**（运行期静默保留：对用户来说空表头优于崩溃） | 新增守卫第 17 项机械比对列数与数组项数；**并纠正了上一轮文档里 `3/7/9/8/5` 的笔误（实际 `3/7/9/9/5`）** |
| C5 | 用户截图：条件行两个下拉收起态显示 `ClauseOption`/`OperatorOption` | **成立**（根因不是 XAML 写错，而是自写模板漏 `ContentTemplateSelector`；`DisplayMemberPath` 由 `ItemTemplateSelector` 实现） | 补该绑定（一行），顺带修好全项目 6 个 `DisplayMemberPath` 下拉；新增守卫第 16 项 |

### 不成立（未采纳）

| # | 意见 | 核实结果 |
|---|---|---|
| C6 | 守卫 `ComboTemplateHasEditableBox` 不可靠（搜索到文件尾、只认字面量 `IsEditable="True"`） | **不成立**（第 5 轮的复审重复了上一轮 B3；该函数在 `7a0b135` 已重写为 `XDocument` 名字域解析，并在 `840+` 行有注释/子模板/后续模板三个自检方向）。另外报告称"守卫的跨词典规则只比数量"同样是上一轮的 B4，`Program.cs:171` 已有集合差比对 |

### 已在 `7a0b135` 修掉（报告基于 `835e3a1`，属时间差）

- `SnapshotView.xaml.cs` 丢掉 `Localization.LanguageChanged` 订阅（本轮再次确认订阅在位）
- 五页签共用 `IsFormOpen` / `ToggleFormCommand` → 已拆成 4 个独立 bool
- `HostOf` 对 `type=SNAPSHOT` 的 `source` 恒空 → 已回退 `repository/snapshot`
- `ParseRecovery` 缺 `ValueKind == Object` 判断 → 已补
- `RetentionText` 解析了没有列显示 → 已加"保留"列；`PhaseCount`/`IsFailed` 死代码 → 已删（`IsDone` 已用于状态圆点）
- 语言切换会把 ES 错误文案覆盖成成功文案 → 已改为只重写成功文案
- 守卫前缀闸门静默放过首段拼错的 key → 已移除闸门 + 白名单
- `SelectedRepository` setter 的两处 fire-and-forget 互相竞争 → 已用 `_suppressSelectedReload` 收口
- 空表头/XAML 英文占位与注释矛盾 → 已改注释；本轮再由守卫第 17 项机械保证
- 保留/统计/分片文本解析时缓存、切语言不重解析 → **仍作为已接受的限制**（ADR-10 代价）

### 经核实属"计数笔误"，不是缺陷

报告称恢复页列数 `8`（`3/7/9/8/5`）。逐列核对为 `9`（Index/Shard/Stage/Type/Source/Target/Files/Bytes/Time），
与 `RecoveryHeaders` 的 9 项一致。守卫第 17 项上线后这类计数不再依赖人工。

## 本轮守则：负向验证也要验"断言本身"

`time_string` 用例的第一次实现只写了 `False(text.Contains("…T00:00:00"))` —— 负向验证（把
`time_string` 改回 `time_millis`）时**它照样通过**，因为时间整个缺失时"不含某串"恒真。
已改为正向断言"格式化后的值必须出现"。教训：**负向验证通过 ≠ 断言有效**，
"只能否定"的断言（不含某串/非空/不为 null）在字段缺失时往往恒真，必须补正向。
（同批的其余 6 条负向验证均按预期精准报错。）

---

# 第 6 轮审查记录

范围：搜索分页（用户反馈"总命中 2570 却只有 10 条"）的实现与自查。

## 自查发现并修掉的问题

| # | 问题 | 处置 |
|---|---|---|
| S1 | `PageSize` setter 直接改 `_pageNum` 字段却不发 `PageNum` 通知（INotifyPropertyChanged 不完整） | 改为在 `RaisePagingChanged()` 里统一补 `PageNum` 通知，`ClearResults` 同路径复用 |
| S2 | 回车提交原用 `<TextBox.InputBindings><KeyBinding Command="{Binding …}"/>`：`InputBinding` 取 DataContext 依赖继承上下文，**本机跑不了 WPF 无法验证它一定生效**，失效时表现为"回车没反应"（鼠标用户不会察觉） | 改为 code-behind `KeyDown` 事件处理器：行为确定，且与本视图既有的 `OnRun`/`OnAddCondition` 风格一致 |
| S3 | 连着翻页时两个请求在途，先发的后到会覆盖新结果（第 4 轮审查在快照页抓到过同类问题） | 引入请求代次号 `_searchGeneration`，过期响应直接丢弃 |
| S4 | 结果集变小后页码越界 → 留下"第 5 / 2 页 + 空表" | 收敛到最后一页并重查一次（`_clampRetry` 保证最多一次，不会递归） |
| S5 | `ClearResults` 只清表格，分页状态残留（清空后仍显示"第 3 / 8 页"，下次搜索还带着旧页码） | 一并复位 `_hasSearched`/`_totalHits`/`_pageNum`/`_gotoText` 并补通知 |
| S6 | 取消勾选 `track_total_hits` 时 ES 返回 `{"value":10000,"relation":"gte"}`，界面显示成"正好 10000"且"下一页"被过早禁用 | 模型加 `TotalHitsIsLowerBound`，显示 `10000+`，`HasNext` 对下限形态按"可能还有"处理 |

## 与源项目（JavaFX 原版）的有意差异

已在 ADR-13 与 QA 第 6 轮写明四点：点搜索回到第 1 页、结果集变小自动收敛、`gte` 显示为下限、
丢弃过期响应。四处都是"源项目的行为在这里会误导用户"，因此**刻意不一致**，并写进了 Windows 复验清单。

## 本轮顺带发现的系统性问题（未修，已登记）

在排查"SearchView 为什么不随语言切换"时发现：**所有缓存页面视图**都在 code-behind 里给控件赋
本地化文案，但只有 SnapshotView（第 4 轮）和 SearchView（本轮）订阅了 `Localization.LanguageChanged`；
`MainViewModel.OnLanguageChanged` 只刷新导航标题与状态栏，页面不会重新 `Loaded`
→ 切语言后其余 8 个页面的 chrome 停在旧语言（中英混排）。

- 本轮**只修 SearchView**：分页条文案就加在这个视图里，不修等于新功能一上线就是坏的语言行为。
- 其余 8 个文件的修复**如实登记为未做**（TASKS 第 6 轮），不假装已解决。
- 但**不允许它继续扩散**：新增守卫规则（页面视图本地化必须订阅）+ 只允许缩短的债务清单
  （`KnownStalePageLocalizers`，修好一个必须删一条，否则守卫报错）。两条负向验证：
  去掉 SearchView 的订阅 → 精准报出该文件；把清单里的 HealthView 修好却不删条目 → 守卫要求删条目。

另发现 31 个 DataGrid 列头硬编码英文（NodesView 13 / ShardsView 8 / IndicesView 7 / RestHistoryWindow 3），
同属 i18n 债务，已登记；不属本次"分页"范围，未动。

## 明确无法在本机验证

- 分页条的真机布局与交互（按钮置灰、下拉收起态文案、跳页输入框回车）——Linux 无 WPF，见 QA 第 25-29 条。
- `from`/`size` 在真实集群上的行为（含 `relation=gte` 与 5000 上限触发 400 的边界）——本机无 ES，
  只有手写 fixture 与官方源码核对。
- 唯一能静态保证的是：分页数学（单测钉死）、DSL 注入形态（解析回读断言）、
  以及"页面不随语言切换"这一缺陷类不再扩散（守卫）。

---

## 第 7 轮（语言切换收尾：8 个缓存页面 + 文案唯一来源）

审查范围：`2b49f0d..工作区`（第 6 轮已登记、本轮修的 i18n 债务）。
审查方式：**主代理自查 + 静态守卫**（子代理审查不可用，同前几轮的流程说明：审查独立性弱于有独立审查子代理的轮次，如实记录）。
审查轴：标准轴（本仓库约定：分层、文案唯一来源、守卫、零警告）+ 规格轴（用户指令"接着把那 8 个页面的语言切换"）。

> **发布说明（首次发布到 GitHub 时）**：本地 16 个提交原本的作者/提交者都是 `dev <dev@local>`，
> 首次发布时按用户要求改为 `Hengkai.Guo <40817154+guohengkai687@users.noreply.github.com>`，
> 用 `git filter-branch --env-filter` 重写了**全部历史** → **所有提交哈希都变了**。
> 重写后逐条比对：**16 个提交的 message 完全一致、与原 ref 相比树内容 diff 为空**（只有作者身份变化）。
> 本文档与 TASKS.md / archive 里引用的哈希已按映射同步为新哈希
> （例：`8d41960`→`835e3a1`、`2690d50`→`7a0b135`、`9f6e0c0`→`2b49f0d`、`bc11ea0`→`042bf1a`）。
> 若在别处（更早的对话或旧报告）看到旧哈希，属重写前的引用。
>
> **发布后调整（默认分支 `main` → `master`）**：按用户要求把远程默认分支改名为 `master`。
> 远端用 GitHub API `POST /repos/{owner}/{repo}/branches/main/rename`（服务端顺带把默认分支指向 `master`，
> 现仅存 `master` 一个分支）；本地 `git branch -m main master`，upstream 与 remote-tracking 改指 `origin/master`。
> **提交内容未变**（仍为 `f5fc931`），克隆地址与 `README` 里的命令不受影响。

<details>
<summary>历史重写的完整哈希映射（旧 → 新，16 个被重写的提交）</summary>

| 旧哈希 | 新哈希 | 提交信息 |
|---|---|---|
| `92d1646` | `9c8cc54` | feat: elastic-desktop-manager WPF 移植（.NET 8） |
| `a554acc` | `9e7e55a` | fix: 审查修复（P0 update-by-query script + P2 九项）+ 36 项回归测试 |
| `da17004` | `38b535b` | fix(连接管理): 修复新增集群后列表不刷新不显示 + HTTPS 地址重复协议 |
| `e8d8f89` | `4759448` | fix(UI): 修复只读属性 TwoWay 绑定报错 + 首页空态不刷新；新增进入页面自动刷新 |
| `8ff117f` | `b012b1e` | fix(UI): 搜索页布局重构、REST 请求体面板加高、SQL 页增加使用说明 |
| `3150fcd` | `4070d2e` | feat(REST): 新增「ES 查询示例」窗口，一键回填方法/路径/请求体 |
| `492e60d` | `50995b5` | feat(UI+能力): 借鉴 ES-King 补齐运维能力 + 现代精致 UI 设计系统 |
| `2d005b4` | `6c4a26f` | fix(启动崩溃): 令牌类型不匹配导致 XamlParseException；补齐守卫的 StaticResource 与令牌类型规则 |
| `d334c11` | `050ed7d` | feat(第3轮): 移除三个功能 + 快照管理 + 搜索索引下拉 + 连接按钮修复 + UI 重新设计 |
| `8d41960` | `835e3a1` | fix(第4轮): 深色底色/索引下拉两个真实缺陷 + 快照页五个列表 |
| `657cbbf` | `666f096` | fix(第4轮补充): 页签错误文案清不掉 + 去掉无用 using |
| `c5d0151` | `ed9ebce` | test(守卫): 新增第 17 项 —— zh/en 同一条词条的占位符必须一致 |
| `2690d50` | `7a0b135` | fix(第4轮审查): 修掉独立审查确认的 5 类问题 + 守卫 17 → 18 项 |
| `19f5738` | `8cedf4e` | fix(第5轮): 下拉收起态显示类型名 + ILM 时间/阶段顺序两个真实缺陷 + 守卫 18→21 |
| `9f6e0c0` | `2b49f0d` | feat(第6轮): 搜索页服务端分页（总命中 2570 却只有 10 行的根因是没有 from/size） |
| `bc11ea0` | `042bf1a` | feat(第7轮): 8 个页面全量语言切换 + 文案唯一来源（并更正上一轮一处错误结论） |

> 本次发布后新增的 2 个提交（`83463b9` LICENSE、`59f87b9` 本文档同步）没有旧哈希 —— 它们诞生于重写之后。
</details>

### 需求达成

用户指令是"接着把那 8 个页面的语言切换"。8 个页面（`EmptyStateView`/`HealthView`/`IndicesView`/`MetricsView`/
`NodesView`/`RestView`/`ShardsView`/`SqlView`）全部改为：抽出 `Localize()`、构造时订阅 `LanguageChanged`、
处理器里 `Localize() + VM.Relocalize()`；`KnownStalePageLocalizers` 债务清单清零（机制保留为棘轮）。
**达成。**

### 本轮自查发现并处置的问题

| ID | 问题 | 处置 |
|---|---|---|
| S1 | 8 个页面只切 chrome、不切 VM 拼装的文案（"共 N 条/第 N 页/指标卡标签"），仍会中英混排 | `PageViewModelBase` 加 `Relocalize()`/`OnRelocalize()`，10 个页面 VM 逐个覆写；守卫强制"订阅了必须调 `Relocalize()`" |
| S2 | `MetricsViewModel.RefreshTitles()` 是**死代码**（第 5 轮留的钩子，从未被调用）——分组标题切语言后不会刷新 | 并入 `OnRelocalize()`（先重发 `RefreshTitle` 通知，再重拼摘要） |
| S3 | `NodesView`/`ShardsView` 的 `SummaryText` **从未绑定** → "节点统计：N"/"分片统计：N" 从来没显示过（VM 一直在算） | 补 `Text="{Binding Summary, Mode=OneWay}"`；顺带把 `$"{...}：{n}"` 里硬编码的全角冒号移进词条（英文界面会显示中文冒号） |
| S4 | 索引页 7 处**双重翻译**：`CreateConfirm`/`ShowJson` 形参约定收 key，调用点却传 `Localization.L(...)` 的结果 → `L()` 查不到就原样返回，**英文界面确认框/JSON 窗标题仍是中文**（中文界面完全正常，因此从未被发现） | 调用点改传 key；补守卫规则（形参以 `Key` 结尾 ⇒ 实参不得是 `L(...)`） |
| S5 | 33 处写死的 `Header=`、7 处写死的 `ToolTip=` 让文案有两个来源 | XAML 全部清掉，改为 code-behind 按当前语言赋值；补守卫规则禁止写死 |
| S6 | `IndicesView` 的 `StringFormat=Total: {0}`：**中英界面都显示 `Total:`**（真·用户可见英文） | 改为词条 `index.total` + VM 派生属性 `TotalText` |
| S7 | `RestHistoryWindow` 的 `Method` 列在 code-behind 里直接赋英文字面量（中英界面都显示 `Method`） | 改用词条 `rest.method`，表头统一走 `HistoryHeaders` 映射 |
| S8 | 守卫的"列数↔表头映射"规则**只保护 SnapshotView**；索引/节点/分片/REST 历史窗 4 张表不在保护范围内 | 规则改为覆盖全部 9 张表，并钉死"必须恰好 9 张"防止悄悄缩小覆盖面；表名统一 `XxxGrid`/`XxxHeaders` |
| S9 | 守卫命名判据有洞：`x:Name="Grid"` 也以 `Grid` 结尾 → 推出空映射名 `Headers`，报错指向"找不到映射"而不是真正的命名问题 | 挡掉无前缀命名并补自检（负向验证 D6） |
| S10 | 重算时机不当会凭空造数据：从没打开过的页面在切语言时显示"节点统计：0" | 加 `_hasData` 门闩（没加载过不重算）；ES 错误原文用 `_indexHintIsError`/`IsError` 排除在重算之外 |

### 对上一轮结论的自我更正（如实记录）

第 6 轮 QA/TASKS 写的"**31 个 DataGrid 列头硬编码英文 → 中文界面中英混排**"**部分是错的**。
本轮逐文件核实：`NodesView`/`ShardsView`/`IndicesView` 的表头在 `Loaded` 时已由 `HeaderMap` 按词条赋成中文，
中文界面里**本来就是中文**，XAML 里的英文只是"被覆盖的占位"——性质是**两处来源**，不是"界面上有英文"。
计数也应为 **33 处**（13+8+9+3，IndicesView 含 2 个模板列），原写 31 处不准。

真正**用户可见**的英文是另外 4 类（均已在 S4-S7 修复）：4 处 `ToolTip="Refresh"`、
`MainWindow` 三个图标提示、`RestHistoryWindow` 的 `Method` 列、`IndicesView` 的 `StringFormat=Total:`。
更正已同步写进 `QA.md` 与 `TASKS.md`（不是悄悄改掉旧文字，而是保留原描述并就地标注更正）。

**教训**：上一轮那条结论是"从 grep 结果直接推断用户可见性"得出的（看到 `Header="Name"` 就断言中文界面会显示英文），
没有追到"code-behind 在 `Loaded` 里有 `HeaderMap` 赋值"这一步。**推断用户可见影响必须先追完赋值链**，
否则会把"代码整洁度问题"报成"用户可见缺陷"，进而误导下一轮的优先级。

### 验证证据

- 构建 **0 警告 0 错误**；Core 单测 **105/105**；静态守卫 **23 → 29/29**（3 条新规则 + 3 条新自检，自检总数 7 → 10）。
- **负向验证 7 条**（改坏 → 确认只有预期那一条失败且信息精准 → 还原 → 复跑全绿）：
  表头映射少一项 / XAML 写死表头 / 把 `L()` 结果当 key 传 / 订阅了却不调 `Relocalize()` /
  债务清单修好却不删条目 / 表名不符合 `XxxGrid` 约定 / 表少一张（规则不许空转缩小覆盖面）。
- 新守卫规则的**自检本身也做了防退化**：`*Key` 参数规则的自检喂了"泛型里的逗号"与"实参里的 lambda"
  两个样本，防止参数下标被数错而漏报。

### 明确无法在本机验证

- **语言切换的真实渲染效果**（整页 chrome、空态文案、确认框、表头）——Linux 无 WPF，见 QA 第 30-36 条。
- **"XAML 不写表头" 的前提**：表头改由 `Loaded` 里赋值，依赖"`Loaded` 在首次渲染之前触发"。
  这与第 5 轮 `SnapshotView`（5 张表、33 列表头全部这么写）已验证过的行为一致，
  但本轮把 4 张新表也纳入同一模式 —— **若真机出现空表头，说明该前提在某个容器里不成立**（QA 第 36 条专门核对这一点）。
- 唯一能静态保证的是：8 个页面都已订阅、订阅者都会叫 VM 重算、文案只有一个来源、
  以及"译文当 key"这一缺陷类不再出现（守卫 + 自检 + 负向验证）。
