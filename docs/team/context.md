# context.md — ES-King 借鉴改造 + 现代精致 UI

## 本轮目标（用户原始诉求）

1. 查看 `dsh-workspace/ES-King-wails` 源码，**参考其设计优点**，改造我们**没有的功能**或**需要优化的地方**。
2. 我们 WPF 的 UI 不够精致，用**现代精致 UI** 理念改造界面。

## 状态：**已完成交付**（build 0/0 · 单测 99/99 · 守卫 8/8）

## 交接物地图

| 文件 | 内容 |
|---|---|
| `SPEC.md` | 需求、范围、AC1–AC12 |
| `ARCHITECTURE.md` | ADR-1~6 + 风险登记 R1–R7 + 未完成清单 |
| `TASKS.md` | 任务拆解与门禁 G1–G4 |
| `REVIEW.md` | 双轴审查：P0/P1/P2 与处置 |
| `QA.md` | 逐条 AC 证据 + 8 项真实缺陷 + Windows 人工核对清单 |
| `archive/` | **上一轮（WPF 移植）的四个产物，勿删** |

## 降级记录（必须保留）

按 skill 的 standard 模式启动了**架构师子代理**，**两次均以无产出失败**：
第一次未写任何文件即失败；按协议 `send_message` 追问一次后再次失败。
依据降级预案（"子代理已结算但无有效产出"属可验证故障）停止该子代理，
**由主代理串行承担架构师角色**并**自查承担审查**。
影响：`ARCHITECTURE.md` 与 `REVIEW.md` 由同一主体产出，**审查独立性弱于上一轮**（上轮有独立审查子代理）。
下一轮建议：如子代理恢复可用，用独立审查子代理复核 `Themes/Common.xaml` 与新页面 XAML。

## 关键背景

- 项目：`/home/kiki/dsh-workspace/elastic-desktop-manager-wpf`（.NET 8 WPF；Core/WPF/Tests/guard 四项目）
- 参考：`/home/kiki/dsh-workspace/ES-King-wails`（Go + Wails + Vue3 + Naive UI，后端 ~70 个 REST 端点）
- **环境硬约束**：开发机 Linux，**WPF 无法运行**。验收只有三样：`dotnet build` 零警告零错误、Core 单测、`tests/binding-guard` 静态守卫。
- **最高风险 R1**：WPF 中 `TextBox.Text`/`CheckBox.IsChecked`/`ComboBox.Selected*`/`ListBox.SelectedItem`/`PasswordBox.Password`
  默认 **TwoWay**，绑到 `private set` 属性会在**运行期**抛异常而**编译零错误**。守卫专治此症。
- **同源风险 R2**：资源 key 缺失——XAML `DynamicResource` 缺失**静默不可见**；C# `FindResource` 缺失**直接崩溃**。

## 本轮完成内容

### Core（能力层，Linux 可单测）
- `Es/EsClient.Operations.cs`（partial 扩展）：A1–A9 端点 —— `_nodes/stats`、Mapping/Settings 读写、
  `_analyze`（含无索引版）、字段 Top 值（terms+cardinality，自动 `.keyword`）、别名增删查、
  `_reindex`、模板查增删（含组件模板）、分配解释/热点线程/线程池/挂起任务、`_forcemerge`。
- `Es/EsMetricsFlattener.cs`（纯函数）：拍平 + 分组 + 稳定排序 + bytes/ms 人类可读格式化。
- `Es/EsParsers.cs` 新增：`ParseAnalyzeTokens`、`ParseTemplates`、`ParseFieldTopValues`、`ParseAliases`。
- `Es/EsQueryHelper.cs` 新增：`ExtractMappingBody`/`ExtractSettingsBody`（让 Mapping/Settings 可"读取→编辑→保存"）。
- `Models/OperationsModels.cs`：`AnalyzeToken`/`EsTemplate`/`EsAlias`/`FieldTopValue`/`FieldTopValuesResult`。

### UI（设计系统 + 新页面）
- `Themes/Tokens.xaml`（新）：间距/圆角/字号/控件高/阴影/等宽字体，`StaticResource`。
- `Themes/Dark.xaml`/`Light.xaml`（重写）：完整语义色板，key 全集一致，正文对比度 ≥ 13.9:1（深）/ 15.8:1（浅）。
- `Themes/Common.xaml`（重写）：全套控件模板含 hover/focus/disabled 三态 + `Expander` + `NavListBoxItemStyle`（选中指示条）。
- `MainWindow.xaml`：图标+文字导航、精修顶栏/状态栏、主题感知 Toast（`OverlayBrush`）。
- 新页面：`MetricsView`（可折叠指标分组 + 筛选）、`AnalyzeView`（分词表格）、
  `DiagnosticsView`（4 页签）、`TemplatesView`（索引/组件模板主从）、`IndexToolsWindow`（5 页签索引工具）。
- `App.xaml`：合并顺序 **Tokens → Common → Light**（顺序错误会在运行期炸，编译不报错）。

### 测试与守卫
- Core 单测 **48 → 99**（新增端点契约、注入路径回归、指标扁平化 11 项、四类解析器、提取助手）。
- 守卫 **3 → 8**：新增"Dark/Light key 一致""XAML 无硬编码颜色""引用资源 key 均已定义""zh/en 词条数量相等"，
  且**每条新规则都有自检**（防假绿）。

## 本轮修掉的真实缺陷（详见 QA.md）

1. **P0 崩溃**：`FindResource("TextBrush")` key 不存在 → 捕获焦点即崩；改 `TryFindResource` + 补守卫规则。
2. **P0 可用性**：`TextBlock` 隐式 Foreground 覆盖继承 → 导航选中态强调色失效；删除隐式 setter。
3. **P1 守卫误报**：同名属性（可写 `QueryCondition.Value` vs 只读展示模型 `Value`）→ 守卫改保守规则 + 自检三方向。
4. **P1 编译**：`AnalyzeTextAsync` 重载歧义 `CS0121` → 改名 `AnalyzeTextWithBuiltinAsync`。
5. **P1 异常泄漏**：非法 JSON 泄漏 `JsonException` → 统一转 `EsException`。
6. **P1 性能**：指标分组默认折叠 + 记忆状态。
7. **P2 编译**：Expander `TargetName="rot"`（`MC4111`）→ 改切 `Path.Data`。

## 未完成（如实声明）

- **A10**：索引数据导出 JSON（带 DSL 过滤）、本地 JSON 批量导入 `_bulk`。
- **B6**：连接管理页卡片化（悬停抬升 + 状态徽章）；现为树形列表（保留文件夹层级）。
- 快照/SLM、ILM、文档完整 CRUD（大块功能，后续批次）。
- 逐条 ES 指标中文说明表（ADR-1 已记录取舍）。
- **真机验证**：无 Windows、无可用 ES 集群 → 视觉与集群联调需用户按 `QA.md` 清单核对。

## 环境备忘

```
NUGET_HTTP_CACHE_PATH=$PWD/.nuget-http-cache NUGET_PACKAGES=$PWD/.packages dotnet build ElasticDesktopManager.sln
NUGET_HTTP_CACHE_PATH=$PWD/.nuget-http-cache NUGET_PACKAGES=$PWD/.packages dotnet run --project tests/ElasticDesktopManager.Tests -c Release
NUGET_HTTP_CACHE_PATH=$PWD/.nuget-http-cache NUGET_PACKAGES=$PWD/.packages dotnet run --project tests/binding-guard -c Release
```
