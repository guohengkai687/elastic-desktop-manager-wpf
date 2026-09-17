# TASKS.md — 任务拆解（WPF 移植）

状态：⏳ 进行中 / ✅ 完成 / ⬜ 待办

| # | 任务 | 状态 |
|---|---|---|
| 1 | 解决方案骨架（sln + Core + WPF + Tests），Linux 下 WPF 可编译 | ✅ |
| 2 | Core: 模型（ConfigProperty/SettingProperty/ES 模型） | ✅ |
| 3 | Core: EsClient（SSL/跳过验证/认证/超时/全部 ES 调用） | ✅ |
| 4 | Core: 存储服务（config/settings/history JSON）+ EDM_DATA_DIR | ✅ |
| 5 | Core: i18n（zh-CN/en）与 JsonHelper | ✅ |
| 6 | UI: App/主题（Light/Dark）/MainWindow 外壳/导航/加载条 | ✅ |
| 7 | UI: 连接管理（树+增删改+测试连接+表单含 SSL 开关） | ✅ |
| 8 | UI: 首页健康 / 节点 / 分片 | ✅ |
| 9 | UI: 索引（分页/搜索/操作菜单/详情弹窗） | ✅ |
| 10 | UI: REST 控制台 + 历史 | ✅ |
| 11 | UI: SQL（游标分页/表格 JSON/CSV 导出） | ✅ |
| 12 | UI: 搜索（构建器/DSL/结果/update-delete by query） | ✅ |
| 13 | UI: 设置 / 关于 | ✅ |
| 14 | Tests: 核心逻辑单测（27/27 通过，Linux） | ✅ |
| 15 | 全量构建修复 + 审查子代理 + blocker 修复 | 🔄 审查进行中 |
| 16 | README / 交付 | ⬜ |