# ARCHITECTURE.md — WPF 移植技术方案

## 目标与范围

把 JavaFX 版 ES 桌面管理器移植为 Windows WPF 应用，保持功能面等价，并新增：
1. **SSL 支持**：连接可选用 `https` 协议（地址为 `host:port` + 显式协议选择，或直接填完整 URL）。
2. **跳过 SSL 验证**：连接级开关 `SkipSslVerify`，勾选时跳过证书链/主机名校验（用于自签名/内网证书）。

## 技术选型

| 项 | 选择 | 理由 |
|---|---|---|
| 运行时 | .NET 8 (LTS)，WPF (`net8.0-windows`) | 环境已有 SDK 6/8/10；WPF 仅 Windows |
| ES 访问 | 自研 `EsClient`（`HttpClient` + `HttpClientHandler`） | 原项目本质是 thin REST 封装；避免引入外部 ES SDK，SSL 跳过逻辑完全可控 |
| JSON | `System.Text.Json` | BCL 内建；`JsonDocument` 解析 + 自定义美化 |
| 存储 | JSON 文件（`config.json`/`settings.json`/`history.json`） | 替代 SQLite+Flyway，逻辑等价、零原生依赖 |
| UI 架构 | MVVM-lite（`INotifyPropertyChanged` + `RelayCommand`），XAML 视图 | 无第三方 MVVM 框架，构建封闭 |
| 主题 | 手写 `Light.xaml`/`Dark.xaml` ResourceDictionary 运行时切换 | 替代 Atlantafx 主题 |
| i18n | Core 内嵌 `Lang.zh_CN`/`Lang.en` 资源 + `L` 静态访问器 | 替代 ResourceBundle |

## 分层

```
ElasticDesktopManager.Core (net8.0, 无 UI 依赖，Linux 可测)
├── Es/EsClient, EsApi, EsResponse        ← SSL/认证/超时/REST 调用
├── Models/ConfigProperty, SettingProperty, EsIndex, EsNode, EsShard, EsSqlResult, EsHealth...
├── Services/ConfigService, SettingService, HistoryService  ← JSON 存储
├── I18n/Localization (L)
└── Json/JsonHelper (美化/解析)

ElasticDesktopManager (net8.0-windows, WPF)
├── App.xaml 启动（读设置→建主题）
├── MainWindow.xaml 外壳（顶栏连接器/主题/设置/关于 + 左导航 + 内容区）
├── ViewModels/*ViewModel
├── Views/*View.xaml
├── Themes/Light.xaml, Dark.xaml, Common.xaml
└── Controls/（JsonBox、LoadingOverlay 等轻量组件）

ElasticDesktopManager.Tests (net8.0 控制台断言)
```

## 关键设计决策（ADR）

- **ADR-1 每个连接一个 HttpClient，生命周期随连接**：`EsClient` 持有 `HttpClientHandler`（内含 skip-ssl 回调与凭据），`HttpClient` 复用以享连接池；切换/断开时 Dispose。与源项目 `CLIENTS` Map 语义一致。
- **ADR-2 跳过验证是连接级、显式、默认关**：避免全局关闭校验的危险默认；连接表单提供“跳过 SSL 验证（自签名证书）”复选框，配置落盘。
- **ADR-3 基址构造**：用户填 `host:port` 或完整 URL；`EsClient` 规整为 `scheme://host:port/`（scheme 取连接配置，缺省补 `http://`）。路径一律以 `/` 开头与基址拼接，避免双斜杠。
- **ADR-4 异步全部 async/await + UI 线程调度**：WPF Dispatcher 上下文自动延续；全局 `IsBusy`/进度指示由 MainWindow 顶部加载条呈现，替代 JavaFX LoadingEvent/ProgressPane。
- **ADR-5 SQL 游标分页**：`/_sql` 返回 `cursor`，下一页 POST `{"cursor":...}`；结果以 `columns`+`rows` 还原为表格，`/took` 等元信息展示在状态栏。与原 `ESSqlScrollResultModel` 一致。
- **ADR-6 JSON 存储用 `EDM_DATA_DIR` 覆盖目录**：默认 `%APPDATA%\ElasticDesktopManager`（Linux 测试用 HOME/.edm），保证测试隔离与 Linux 可测。
- **ADR-7 REST 历史限 100 条**（与源触发器一致），存 method/url/body/时间。
- **ADR-8 索引健康列用圆点着色**（green/yellow/red），与源一致。
- **ADR-9 简易查询构建器**：bool 子句（must/should/must_not/filter）× 条件行（字段+操作符 term/range/match/wildcard/exists+值）→ 生成 DSL JSON；不照搬 JavaFX 复杂树组件，功能面等价（源项目的图形化构建器核心即此子集）。

## 页面映射

| JavaFX (FXML) | WPF View |
|---|---|
| es_home.fxml + es_cluster_health_monitor.fxml | MainWindow + HealthView |
| es_config / es_config_form / es_config_form_folder | ConnectionsView + 对话框 |
| es_cluster_node.fxml | NodesView |
| es_cluster_sharding.fxml | ShardsView |
| es_cluster_index.fxml | IndicesView |
| es_cluster_rest.fxml + es_cluster_rest_history.fxml | RestView + 历史窗口 |
| es_cluster_sql.fxml | SqlView |
| es_cluster_search.fxml | SearchView |
| es_setting.fxml | SettingsView |
| es_about_me.fxml | AboutView |

## 风险与缓解

- Linux 上无法运行 WPF → 以编译 + Core 单测为门禁；README 说明 Windows 运行方式。
- XAML 编译细节多 → 尽早骨架验证（第 2 步）再铺量。