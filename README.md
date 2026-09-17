# Elastic Desktop Manager (WPF 版)

基于 **WPF (.NET 8)** 的 Elasticsearch 桌面查询管理客户端，由开源项目
[elastic-desktop-manager](https://github.com/lxwise/elastic-desktop-manager)（JavaFX 实现）移植而来。
功能面与源项目对齐，并**新增 SSL 支持与“跳过 SSL 验证”**能力。

> 目标环境：Windows 10/11（WPF 仅支持 Windows）。源码可在 Linux 上完成编译验证（`EnableWindowsTargeting`）。

## 功能

| 模块 | 说明 |
| --- | --- |
| 连接管理 | 文件夹 + 集群树，增删改查、过滤、测试连接、连接/断开 |
| 首页健康 | 集群健康状态、节点/分片指标、ES 版本信息，30s 自动刷新 |
| 节点 | `_cat/nodes` 全字段表格，可按名称/IP 过滤 |
| 分片 | `_cat/shards` 表格 + 状态统计 |
| 索引 | 列表（分页/搜索）、健康色点、详情/状态查看、刷新/Flush/清缓存/打开/关闭 |
| REST API | 任意方法/路径/请求体执行、JSON 格式化、响应美化、历史记录（100 条） |
| SQL 查询 | `/_sql` 执行、fetch_size 游标分页、表格/JSON 双视图、CSV 导出 |
| 搜索 | 图形化条件构建器（must/should/must_not/filter × term/match/wildcard/prefix/range/exists）→ 生成 DSL，结果表格/JSON，支持按查询更新/删除 |
| 设置 | 语言（简中/English）、主题（浅色/深色/跟随系统）、请求超时、关闭行为 |
| 关于 | 版本与项目信息 |

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
│   ├── Es/        EsClient（SSL/认证/超时/REST 调用）、EsParsers、EsSession、EsException
│   ├── Models/    ConfigProperty（含 Scheme/SkipSslVerify）、SettingProperty、ES 数据模型
│   ├── Services/  ConfigService / SettingService / CommandHistoryService（JSON 存储）
│   ├── I18n/      Localization（zh_CN 默认 + en）
│   └── Json/      JsonHelper（美化/解析）
├── src/ElasticDesktopManager/          net8.0-windows WPF 应用
│   ├── Views/       各页面与对话框（XAML）
│   ├── ViewModels/  MVVM 视图模型
│   ├── Themes/      浅色/深色主题 ResourceDictionary
│   ├── Mvvm/        ObservableObject / RelayCommand / AsyncRelayCommand
│   └── Services/    ThemeService / Ui / SystemTheme
└── tests/ElasticDesktopManager.Tests/  net8.0 控制台断言测试（Linux 可直接运行）
```

## 构建与运行

要求：.NET SDK 8.0+（Windows 上直接 `dotnet` 即可；Linux 编译需要能从 nuget.org 还原 Windows 桌面包）。

```bash
# 还原并构建（Linux 亦可）
dotnet build ElasticDesktopManager.slnx

# 运行单元测试（Core 逻辑，Linux 可执行）
dotnet run --project tests/ElasticDesktopManager.Tests -c Release

# 运行应用（Windows）
dotnet run --project src/ElasticDesktopManager
```

数据目录：`%APPDATA%\ElasticDesktopManager\`（配置 `config.json`、设置 `settings.json`、历史 `history.json`）。
测试/开发可用环境变量 `EDM_DATA_DIR` 覆盖数据目录。

> 本仓库附加 `nuget.config`，把 NuGet 包缓存指向仓库内 `.packages/`，便于受限环境离线/隔离构建。

## 与 JavaFX 原版的差异（移植说明）

- 存储：SQLite + Flyway → JSON 文件（等价语义：配置树/设置/命令历史限 100 条）。
- ES 客户端：High Level REST Client → 自研 `HttpClient` 薄封装（REST 端点与源项目一致）。
- 主题：Atlantafx 主题包 → 手写浅/深色 ResourceDictionary。
- 搜索构建器：源项目复杂树组件 → 布尔子句 × 条件行生成 DSL（核心功能等价）。
- 自动更新检测：未移植（不联网）。
- 平台：原项目跨平台，本移植仅限 Windows（WPF 本质）。

## 免责声明

本项目为开源个人工具，仅用于学习与研究；请勿用于任何非法用途。软件不采集、不上传任何用户数据。