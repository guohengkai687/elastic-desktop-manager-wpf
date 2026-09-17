# QA.md — 验收执行记录（WPF 移植）

执行环境：Linux（无法运行 WPF GUI；以编译 + Core 单测 + 静态核验为门禁）。

| AC | 验收项 | 证据 | 结果 |
| --- | --- | --- | --- |
| 1 | 三项目解决方案，Linux 下零错误构建 | `dotnet build ElasticDesktopManager.sln` → Build succeeded, 0 Warn 0 Err | ✅ |
| 2 | EsClient 支持 http/https；SkipSslVerify 时跳过证书校验，否则默认校验 | 单测 SSL ×3（回调安装决策）+ URL ×6；CreateHandler 实现；表单→落盘→handler 闭环 | ✅ |
| 3 | Basic 认证与超时 | 单测 Basic 头 ×2 / 超时翻译 ×1 | ✅ |
| 4 | REST 端点覆盖与源一致 | EsClient 方法清单 vs ElasticManage.java 静态比对（另修正 Java 两处 URL 漏斜杠 bug） | ✅ |
| 5 | 连接管理：文件夹+集群树、CRUD、测试、连接、过滤、启动开关 | ConnectionView/Forms/VM 静态核验；删除当前连接联动断开（P2-13 修复） | ✅ |
| 6 | 视图齐全 | 全部视图编译通过；update by query 补 script 输入（P0-1 修复）+ 单测 ×4 | ✅ |
| 7 | JSON 存储 + EDM_DATA_DIR + 历史限 100 | 单测 存储 ×5（含损坏备份/原子写回归） | ✅ |
| 8 | 单元测试全绿 | `dotnet run --project tests` → **36 passed / 0 failed** | ✅ |
| 9 | 双语言 i18n | 单测 i18n ×3；validate.required/common.delete 补齐；5 处硬编码改词条 | ✅ |

补充：SSL 徽标（跳过验证时顶栏黄色「⚠ 跳过 SSL 验证」）静态核验；审查 REVIEW.md 已并入（P0 修复后 AC6 达标）。

最终基线（交付前复验）：`dotnet build` 0 警告 0 错误；`dotnet run --project tests -c Release` 36/36 通过。