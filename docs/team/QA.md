# QA.md — 验收执行记录（WPF 移植）

执行环境：Linux（无法运行 WPF GUI；以编译 + Core 单测 + 静态核验为门禁）。

| AC | 验收项 | 证据 | 结果 |
| --- | --- | --- | --- |
| 1 | 三项目解决方案，Linux 下零错误构建 | `dotnet build ElasticDesktopManager.sln` → Build succeeded, 0 Warn 0 Err | ✅ |
| 2 | EsClient 支持 http/https；SkipSslVerify 时跳过证书校验，否则默认校验 | 单测 SSL: 默认不跳过→回调为空 / SkipSslVerify=true→回调已安装 / false→为空；CreateHandler 实现 | ✅ |
| 3 | Basic 认证与超时 | 单测 请求: Basic 认证头 / 请求: 超时转为可读错误（1s 超时触发） | ✅ |
| 4 | REST 端点覆盖与源一致 | EsClient 方法清单 vs ElasticManage.java（health/nodes/shards/indices/sql/search/rest/index 操作）静态比对 | ✅ |
| 5 | 连接管理：文件夹+集群树、CRUD、测试、连接、过滤、启动开关 | ConnectionView/ConnectionFormDialog/FolderFormDialog/ConnectionsViewModel 静态核验 | ✅ |
| 6 | 视图齐全 | Health/Nodes/Shards/Indices/Rest(+History)/Sql/Search/Settings/About 均已实现并编译 | ✅ |
| 7 | JSON 存储 + EDM_DATA_DIR + 历史限 100 | 单测 存储: 配置增删改+级联删除 / 历史记录上限 100 | ✅ |
| 8 | 单元测试全绿 | `dotnet run --project tests` → 27 passed / 0 failed | ✅ |
| 9 | 双语言 i18n | 单测 i18n: zh/en 切换；词典静态核验 | ✅ |

补充：主窗口顶栏在当前连接开启“跳过 SSL 验证”时显示黄色徽标（MainWindow.xaml，静态核验）。

待办：并入 REVIEW.md 的规格轴结论与 blocker 处置结果（审查完成后更新本表）。