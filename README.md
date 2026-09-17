# Elastic Desktop Manager (WPF 版)

基于 **WPF (.NET 8)** 的 Elasticsearch 桌面查询管理客户端，由 **lxwise** 的开源项目
[elastic-desktop-manager](https://github.com/lxwise/elastic-desktop-manager)（JavaFX 实现，Apache-2.0 许可）移植而来。
功能面与源项目对齐，并**新增 SSL 支持与“跳过 SSL 验证”**能力。

> 本仓库（WPF 移植版）的作者是 **guohengkai**；`lxwise` 是上游 JavaFX 原版的作者，此处保留署名与链接以符合 Apache-2.0 的署名要求。

> 目标环境：Windows 10/11（WPF 仅支持 Windows）。源码可在 Linux 上完成编译验证（`EnableWindowsTargeting`）。

## 功能

| 模块 | 说明 |
| --- | --- |
| 连接管理 | 文件夹 + 集群树，增删改查、过滤、测试连接、连接/断开 |
| 首页健康 | 集群健康状态、节点/分片指标、ES 版本信息，30s 自动刷新 |
| 节点 | `_cat/nodes` 全字段表格，可按名称/IP 过滤 |
| 分片 | `_cat/shards` 表格 + 状态统计 |
| 索引 | 列表（分页/搜索）、健康色点、详情/状态查看、刷新/Flush/清缓存/打开/关闭，**索引工具**（Mapping/Settings 在线查看与更新、别名管理、字段 Top 值、Force Merge、Reindex 迁移） |
| 指标 | `_nodes/stats` 全量指标，按前缀分组可折叠、可按 key/值/节点筛选，字节与时长自动人性化 |
| REST API | 任意方法/路径/请求体执行、JSON 格式化、响应美化、历史记录（100 条）、**ES 查询示例一键回填** |
| SQL 查询 | `/_sql` 执行、fetch_size 游标分页、表格/JSON 双视图、CSV 导出 |
| 搜索 | 索引下拉（可编辑：选具体索引，也能直接输入 `logs-*` 通配符/别名/多索引，并显示加载数量或失败原因）；图形化条件构建器（must/should/must_not/filter × term/match/wildcard/prefix/range/exists）→ 生成 DSL，结果表格/JSON，**服务端分页**（10/20/30/50/100 条每页、首页/上下页/末页/跳页，行为对齐源项目 PagingControl），支持按查询更新（需填写 Painless 更新脚本）/按查询删除 |
| 快照 | 五个列表：**仓库管理**（列出/新建/校验/删除）、**快照管理**（列出/创建/查看 JSON/删除）、**快照恢复**（可选索引 + 重命名正则 + 分片级恢复进度）、**自动策略 SLM**（列出/新建/立即执行/删除）、**生命周期 ILM**（列出/新建/删除）。创建与恢复用 `wait_for_completion=false` 不阻塞界面；SLM/ILM 属 x-pack 能力，集群不支持时错误只显示在对应页签内（不会弹模态框） |
| 设置 | 语言（简中/English，**全界面即时切换**：页面 chrome 与"共 N 条/第 N 页"这类 VM 拼装的动态文案一起切）、主题（浅色/深色/跟随系统）、请求超时、关闭行为 |
| 关于 | 版本与项目信息 |

进入 **节点 / 分片 / 索引 / 指标 / 快照** 等页面时会自动刷新一次数据（静默模式：不占全局忙碌条、失败不弹模态错误框，避免多页同时刷新时刷屏）；手动「刷新」按钮仍会显示忙碌与错误提示。连接建立或断开时，所有已打开的页面会同步刷新连接状态。

## 界面设计

界面基于一套**设计令牌**构建，浅色/深色两套主题的色值 key 完全一致（由守卫强制校验）：

- **几何令牌** `Themes/Tokens.xaml`：间距（4 的倍数）、固定圆角、字号、等宽字体、网格尺寸 —— 用 `StaticResource`（不随主题变化）。
- **主题性格令牌** 控件圆角 / 控件高度 / 卡片圆角 / 卡片内边距 / 阴影 —— 定义在 `Light.xaml`、`Dark.xaml` 里并用 `DynamicResource`：
  浅色=现代精致（圆角 8/12、控件高 32、内边距 18、柔和阴影）；深色=专业工具（圆角 4/6、控件高 28、内边距 12、几乎无投影、更低对比描边）。
- **矢量图标** `Core/Ui/AppIcons.cs`（24×24 描边路径，Core 侧纯字符串）+ `Services/AppIconGeometries.cs`（Geometry）：
  不依赖字体回退（旧版用 ❤ ⬡ ✂ 这类字形，语义不一致且可能显示成方框），描边色随选中态变化。
  路径语法由 Core 单测逐条校验（命令集合 / M 开头 / 坐标范围 / 整圆弧成对），避免运行期 `Geometry.Parse` 抛异常。
- **语义色板** `Themes/Dark.xaml` / `Light.xaml`：按「底层 → 卡片 → 次层 → 浮层」分层，文本分「正文/次要/提示」三级，
  强调色含淡底与淡底上的可读文字色 —— 用 `DynamicResource`，支持运行时切主题。
  关键对比度：深色正文本 13.9:1、浅色正文本 15.8:1（均超 WCAG AA 4.5:1）。
- **控件模板** `Themes/Common.xaml`：按钮 / 输入框 / 密码框 / 下拉框 / 复选 / 单选 / 列表 / 树 / 页签 / 表格 /
  滚动条 / 进度条 / 提示 / 折叠面板，**每个可交互控件都覆盖 hover、focus、disabled 三态**。
- **导航**：分组标题（概览 / 集群 / 数据 / 工具）+ 矢量图标 + 文字，选中项带左侧强调指示条与淡底。
- **窗口背景**：窗口基样式 `WindowBaseStyle` 必须被每个窗口**显式引用** —— WPF 的隐式样式按控件具体类型查资源，
  `TargetType="Window"` 的隐式样式不会作用到 `MainWindow`/`SettingsWindow` 这类派生窗口（dotnet/wpf#10461），
  后果是客户区一直用系统默认的白色底（浅色主题看不出来，深色主题下就是一大片刺眼白底）。

> 注意：`App.xaml` 中资源字典的合并顺序必须是 **Tokens → Common → Light**。
> `Common.xaml` 用 `StaticResource` 引用令牌，顺序反了会在**运行期**解析失败（编译期不报错）。
>
> ⚠️ 令牌的声明类型必须与目标属性**精确一致**：间距是 `Thickness`、`TitleBarHeight` 等网格尺寸是 `GridLength`、
> 字号与控件高是 `Double`。**不要**为了整齐把它们统一写成 `sys:Double` —— WPF 的 `GridLengthConverter` /
> `ThicknessConverter` 只接受字符串，类型不符会在**启动时**抛 `XamlParseException`（编译期 0 错误 0 警告）。
> 守卫的「令牌类型匹配」规则会静态拦截。

## ES 查询示例（REST API 页）

REST API 页工具栏的 **ES 查询示例** 按钮会打开示例窗口，内置常用查询 DSL，可一键回填到方法 / 路径 / 请求体：

| 分类 | 示例 |
| --- | --- |
| 全文匹配 (match) | `match`、`match_phrase`、`multi_match` |
| 精确匹配 (term) | `term`、`terms`、`ids`、`exists` |
| 范围查询 (range) | 数值 `range`、日期 `range`（`now-7d/d`） |
| 组合查询 (bool) | `bool`（must/filter/must_not/should）、`wildcard` |
| 排序与分页 | 多字段 `sort`、`_source` 字段过滤 |
| 聚合统计 (aggs) | `terms` 聚合、`stats` 聚合 + 子聚合 |
| 写入与修改 | 写入文档、更新文档、`_bulk`、`_delete_by_query` |
| 索引管理 | `_cat/indices?v`、`_mapping` |

使用方式：

1. 打开示例窗口，左侧按分类列出示例，可用顶部输入框按**标题 / 说明 / 方法 / 路径 / 正文**模糊筛选。
2. 选中示例后，右侧显示将写入的 **方法 / 路径 / 请求体** 预览。
3. **索引名** 输入框用于替换示例中的 `{index}` 占位符（留空则用 `index_name`）；预览会实时更新。
4. 点 **应用** 回填到 REST 页并关闭窗口（方法、路径、请求体三者一起替换）。

> 示例中的 `{index}` 会按你填写的索引名替换；`_bulk` 示例是 NDJSON（每两行为一组，行尾需换行），批量写 `_bulk` 时索引名直接写在正文里。

## SQL 查询页使用说明

页面顶部有可折叠的「使用说明」，此处为同样内容的文字版：

1. 在输入框写标准 SQL（Elasticsearch SQL 语法），例如：
   ```sql
   SELECT * FROM record_secu LIMIT 20
   SELECT name, age FROM users WHERE age > 30 ORDER BY age
   SELECT COUNT(*) FROM my-index
   ```
2. **每批行数**对应 Elasticsearch 的 `fetch_size`，即每次从集群取回多少行（游标分页）。
3. 点 **执行** 返回首批结果；结果超出一页时用 **下一页** 沿游标继续读取后续批次。
4. **上一页** 会先关闭当前游标，再从第一页重新查询（ES 游标只能向前，无法真正回退）。
5. **导出 CSV** 把当前已读取到的行导出为 CSV（带 UTF-8 BOM，Excel 可直接打开）。
6. 结果有「表格」与「JSON」两个页签；聚合类查询的完整结构请查看 JSON 页签。

## SSL 支持与“跳过 SSL 验证”

- **SSL 支持**：新建/编辑连接时可选择协议 **HTTP** 或 **HTTPS（SSL）**；地址也可直接填写完整 URL（`https://es.example.com:9200`），客户端按 `scheme://host:port/` 规整基址。
- **跳过 SSL 验证**：连接表单提供「跳过 SSL 验证（自签名证书）」复选框：
  - 勾选后，`EsClient` 通过 `HttpClientHandler.ServerCertificateCustomValidationCallback = DangerousAcceptAnyServerCertificateValidator` 跳过证书链与主机名校验（适用于自签名证书/内网 CA）；
  - 不勾选时保持 .NET 默认证书校验（生产安全默认值）；
  - 配置随连接持久化；当前连接若开启了跳过验证，主窗口顶栏会显示黄色「⚠ 跳过 SSL 验证」徽标。
- 认证：启用安全认证后按 `Basic` 方式附带用户名/密码；超时可在设置中配置。

## 项目结构

```
elastic-desktop-manager-wpf/
├── src/ElasticDesktopManager.Core/     net8.0 类库（无 UI 依赖，Linux 可测）
│   ├── Es/        EsClient（SSL/认证/超时/REST 调用，运维端点见 EsClient.Operations.cs）、
│   │              EsParsers、EsSession、EsException、EsQueryHelper、EsQueryExample（查询示例目录）、
│   │              EsMetricsFlattener（指标扁平化/分组/格式化，纯函数）
│   ├── Models/    ConfigProperty（含 Scheme/SkipSslVerify）、SettingProperty、ES 与运维数据模型
│   ├── Services/  ConfigService / SettingService / CommandHistoryService（JSON 存储）
│   ├── I18n/      Localization（zh_CN 默认 + en，两套词条严格对齐）
│   └── Json/      JsonHelper（美化/解析）
├── src/ElasticDesktopManager/          net8.0-windows WPF 应用
│   ├── Views/       各页面与对话框（XAML）
│   ├── ViewModels/  MVVM 视图模型
│   ├── Themes/      Tokens（几何令牌）+ Dark/Light（语义色板）+ Common（控件模板）
│   ├── Mvvm/        ObservableObject / RelayCommand / AsyncRelayCommand
│   └── Services/    ThemeService / Ui / SystemTheme
└── tests/ElasticDesktopManager.Tests/  net8.0 控制台断言测试（Linux 可直接运行）
    tests/binding-guard/               XAML 绑定契约 + 资源 key + 令牌类型 + 派生属性通知 + 控件模板契约 + i18n 静态守卫（31 项，Linux 可跑）
```

## 构建与运行

要求：.NET SDK 8.0+（Windows 上直接 `dotnet` 即可；Linux 编译需要能从 nuget.org 还原 Windows 桌面包）。

```bash
# 获取源码
git clone https://github.com/guohengkai687/elastic-desktop-manager-wpf.git
cd elastic-desktop-manager-wpf

# 还原并构建（Linux 亦可）
dotnet build ElasticDesktopManager.sln

# 运行单元测试（Core 逻辑，Linux 可执行）
dotnet run --project tests/ElasticDesktopManager.Tests -c Release

# XAML 绑定契约 + 资源 key + 令牌类型 + 派生属性通知 + 控件模板契约 + i18n 静态守卫（31 项，含可失败自检）
# 覆盖：只读属性绑到默认 TwoWay 目标 / Dark·Light 主题 key 不对称 / 硬编码颜色 /
#      引用了不存在的资源 key（DynamicResource + StaticResource）/ 资源引用嵌在字符串中 /
#      设计令牌类型与目标属性不匹配（如 Double 用于 GridLength/Thickness，会启动即崩）/
#      被 XAML 绑定的只读派生属性漏发 PropertyChanged（按钮会永久禁用）/ 资源字典字面量下标（切主题会拆掉样式）/
#      自写控件模板漏掉契约部件或契约绑定（PART_EditableTextBox / ContentTemplateSelector）/
#      DataGrid 列数与 code-behind 表头映射项数不一致（越界是静默跳过，只丢表头）/
#      中英文词条数量不等或占位符不一致 / 代码里用到的 i18n key 不存在 /
#      页面视图在 code-behind 里本地化却没订阅语言切换（切语言整页停在旧语言）/
#      订阅了语言切换却没让 VM 重算"共 N 条"这类缓存文案（标题切了、统计还是旧语言）/
#      XAML 里写死 Header/ToolTip 文案（两个来源早晚对不上）/ 形参收 key 却传了 L() 的译文（双重翻译）
#      代码里的 GitHub 仓库链接指向别人的项目（关于窗口把用户带到上游仓库，不是本仓库）
dotnet run --project tests/binding-guard -c Release

# 运行应用（Windows）
dotnet run --project src/ElasticDesktopManager
```

数据目录：`%APPDATA%\ElasticDesktopManager\`（配置 `config.json`、设置 `settings.json`、历史 `history.json`）。
测试/开发可用环境变量 `EDM_DATA_DIR` 覆盖数据目录。

> 本仓库附加 `nuget.config`，把 NuGet 包缓存指向仓库内 `.packages/`，便于受限环境离线/隔离构建。

## 与 JavaFX 原版的差异（移植说明）

- 存储：SQLite + Flyway → JSON 文件（等价语义：配置树/设置/命令历史限 100 条）。
- ES 客户端：High Level REST Client → 自研 `HttpClient` 薄封装（REST 端点与源项目一致，并**修正了源项目索引 refresh/flush/清缓存三处 URL 漏斜杠 bug**）。
- 主题：Atlantafx 主题包 → 手写浅/深色 ResourceDictionary。
- 搜索构建器：源项目复杂树组件 → 布尔子句 × 条件行生成 DSL（核心功能等价）；「按查询更新」需填写 Painless 更新脚本（源项目有脚本编辑表）。
- 自动更新检测：未移植（不联网）。
- 平台：原项目跨平台，本移植仅限 Windows（WPF 本质）。
- 启动时打开连接对话框默认开启（源项目 DB 默认值 openDialog=1）；请求超时一个设置项同时作用于普通请求与 SQL（与源项目行为一致）。

## 免责声明

本项目为开源个人工具，仅用于学习与研究；请勿用于任何非法用途。软件不采集、不上传任何用户数据。

## 许可

本仓库是 [lxwise/elastic-desktop-manager](https://github.com/lxwise/elastic-desktop-manager)（Apache-2.0 许可）的
WPF 移植版，因此采用**两个许可并存**的结构：

| 范围 | 许可 | 全文 |
| --- | --- | --- |
| 本仓库自有代码（WPF 移植与新增功能） | **MIT** © 2026 Hengkai.Guo | [`LICENSE`](LICENSE) |
| 移植自上游 JavaFX 原版的部分 | **Apache License 2.0** | [`LICENSE-APACHE`](LICENSE-APACHE) |

上游项目的版权与署名、以及移植时所做的重大修改，见 [`NOTICE`](NOTICE)；
逐项功能对照见上文「与 JavaFX 原版的差异（移植说明）」。

> **使用提示**（非法律意见）：MIT 与 Apache-2.0 相互兼容，但 Apache 部分要求**保留版权与署名声明**、
> **随附许可证副本**并**注明修改**。若你分发本软件或其衍生版本，请把 `LICENSE`、`LICENSE-APACHE`、
> `NOTICE` 三个文件一并保留。
