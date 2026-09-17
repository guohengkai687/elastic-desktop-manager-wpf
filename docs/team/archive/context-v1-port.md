# context.md — elastic-desktop-manager WPF 移植

- 目标：把 `elastic-desktop-manager-master`（JavaFX 21 + ES HighLevel REST Client 的 ES 桌面管理器）移植为 WPF (.NET 8) 应用，**新增 SSL 支持与“跳过 SSL 验证”功能**。
- 源项目：`/home/kiki/dsh-workspace/elastic-desktop-manager-master`（102 个 Java 文件、15 个 FXML、SQLite+Flyway 存储）。
- 本实现：`/home/kiki/dsh-workspace/elastic-desktop-manager-wpf/`
- 规模选择：**standard 模式**（架构设计在会话中基于源码通读完成，实现由主代理完成以保持集成一致性；审查阶段由独立子代理执行——skill 的核心闭环保留）。
- 环境约束：本机为 Linux（无 Windows），WPF 项目以 `EnableWindowsTargeting` 编译验证；运行需 Windows。EsClient/模型/服务放在 net8.0 Core 库，测试在 Linux 上直接执行。

## 可测验收清单（AC）

1. 解决方案包含 Core（net8.0）、WPF（net8.0-windows）、Tests（net8.0 控制台）三项目，`dotnet build` 在 Linux 上零错误通过。
2. EsClient 支持 `http`/`https` 基址；`SkipSslVerify=true` 时通过 `ServerCertificateCustomValidationCallback` 跳过证书校验，`false` 时保持默认校验（SSL 功能 AC）。
3. Basic 认证（用户名/密码）、超时可配置（来自设置），请求超时生效。
4. 对 ES 的 REST 调用与源项目一致：`/_cluster/health`、`/_cat/indices`、`/_cat/nodes`、`/_cat/shards`、`/_sql`（cursor 分页）、`/_search`、`_update_by_query`、`_delete_by_query`、索引操作（详情/统计/refresh/flush/clear cache/open/close）。
5. 连接管理：文件夹+集群树，增删改查、测试连接、连接、过滤，启动时可选择是否打开连接对话框。
6. 视图齐全：首页健康、节点、分片、索引（分页+搜索+操作）、REST 控制台（历史）、SQL（表格/JSON + 游标分页 + CSV）、搜索（简易条件构建器 + DSL JSON + 结果表 + update/delete by query）、设置（语言/主题/超时/关闭行为）、关于。
7. 存储：JSON 文件（配置/设置/命令历史，历史限 100 条），数据目录支持 `EDM_DATA_DIR` 覆盖（测试用）。
8. 单元测试覆盖：URL 规整、SSL skip 决策、SQL 请求体构造、配置服务增删改、索引解析、JSON 美化，全部在 Linux `dotnet run` 通过。
9. 双语言 i18n（zh-CN 默认 + en），设置页可切换。

## 当前阶段

**已完成交付**：
1. 独立子代理审查（标准轴：0 崩溃/0 数据损坏/0 SSL 失效；规格轴 AC1-9 核对）→ REVIEW.md 全量处置。
2. P0-1（按查询更新静默空操作）修复：新增 Painless 更新脚本输入 + `EsQueryHelper`（Core 可测），无脚本禁止更新流程。
3. P2 高优先 9 项已修复（历史空态/i18n 漏词硬编码/cursor 关闭/CSV BOM/GET body/存储原子写+损坏备份/协议显示重复/健康轮询随可见性暂停/删除当前连接联动断开），其余以说明处置。
4. 回归测试 27 → 36 项，全部通过；构建 0 警告 0 错误；`git init` 首次提交（9c8cc54）。
5. README / QA.md 齐备。