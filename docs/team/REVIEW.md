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
