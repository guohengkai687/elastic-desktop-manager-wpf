namespace ElasticDesktopManager.Core.Ui;

/// <summary>
/// 应用图标：24×24 视图框的**描边式矢量路径**（路径迷你语言）。
///
/// 为什么不用字体字形（如 Segoe MDL2 或 ❤ ⬡ ✂ 这类符号）：
///   1. 文本字形依赖字体回退，缺字体时会显示成方框，且无法在本机（Linux）验证；
///   2. 首版用了 ❤ ⬡ ✂ ⚕ ❐ 这类零散符号，语义不一致、笔画粗细不一，
///      是"界面看起来不专业"的最主要来源（已由用户截图确认）；
///   3. 矢量路径跨环境确定性渲染，粗细/圆角/颜色完全可控（可随主题换描边色）。
///
/// 约定（务必遵守，测试会校验）：
///   · 只用 M / L / H / V / C / A / Z 基础命令，避免相对命令与平滑曲线歧义；
///   · 所有坐标落在 0..24 之间，配合 Stretch=Uniform 保证视觉大小一致；
///   · 圆形一律用"两段半圆弧"写法（A rx ry 0 1 0 ...），这是最稳的整圆写法；
///   · 数据放在 Core（无 WPF 依赖）以便在 Linux 上做语法与范围校验。
/// </summary>
public static class AppIcons
{
    // ---- 导航 ----

    /// <summary>首页 / 集群健康。</summary>
    public const string Home =
        "M3.5 10.6 L12 3.6 L20.5 10.6 M5.9 9.1 V20 H18.1 V9.1 M9.8 20 V14.2 H14.2 V20";

    /// <summary>节点（两层服务器）。</summary>
    public const string Nodes =
        "M4.6 4.6 H19.4 V9.6 H4.6 Z M4.6 14.4 H19.4 V19.4 H4.6 Z M7.8 7.1 H7.84 M7.8 16.9 H7.84";

    /// <summary>分片（2×2 网格）。</summary>
    public const string Shards =
        "M4.6 4.6 H11 V11 H4.6 Z M13 4.6 H19.4 V11 H13 Z M4.6 13 H11 V19.4 H4.6 Z M13 13 H19.4 V19.4 H13 Z";

    /// <summary>索引（数据库圆柱）。</summary>
    public const string Indices =
        "M12 3.8 C16.3 3.8 19.4 4.9 19.4 6.3 C19.4 7.7 16.3 8.8 12 8.8 C7.7 8.8 4.6 7.7 4.6 6.3 " +
        "C4.6 4.9 7.7 3.8 12 3.8 Z " +
        "M4.6 6.3 V17.7 C4.6 19.1 7.7 20.2 12 20.2 C16.3 20.2 19.4 19.1 19.4 17.7 V6.3 " +
        "M4.6 12 C4.6 13.4 7.7 14.5 12 14.5 C16.3 14.5 19.4 13.4 19.4 12";

    /// <summary>指标（柱状图）。</summary>
    public const string Metrics =
        "M4 20.4 V3.6 M4 20.4 H20.4 M8.3 20.4 V13.8 M12.2 20.4 V8.4 M16.1 20.4 V11.2";

    /// <summary>搜索（放大镜）。</summary>
    public const string Search =
        "M4.8 10.9 A6.1 6.1 0 1 0 17 10.9 A6.1 6.1 0 1 0 4.8 10.9 Z M15.4 15.4 L20 20";

    /// <summary>快照（相机）。</summary>
    public const string Snapshot =
        "M4.6 8.2 H8.3 L9.8 5.6 H14.2 L15.7 8.2 H19.4 V19.2 H4.6 Z " +
        "M12 10.4 A3.3 3.3 0 1 0 12 17 A3.3 3.3 0 1 0 12 10.4 Z";

    /// <summary>REST API（终端窗口）。</summary>
    public const string Rest =
        "M3.6 5 H20.4 V19 H3.6 Z M7.4 9.9 L9.9 12.4 L7.4 14.9 M12.7 15.5 H16.5";

    /// <summary>SQL 查询（表格）。</summary>
    public const string Sql =
        "M3.6 5 H20.4 V19 H3.6 Z M3.6 9.9 H20.4 M9.7 5 V19";

    // ---- 顶栏与操作 ----

    /// <summary>主题（半明半暗）。</summary>
    public const string Theme =
        "M12 3.6 A8.4 8.4 0 1 0 12 20.4 A8.4 8.4 0 1 0 12 3.6 Z M12 3.6 V20.4";

    /// <summary>设置（推子）。</summary>
    public const string Settings =
        "M3.6 8 H7.7 M12.3 8 H20.4 M3.6 16 H12.3 M16.9 16 H20.4 " +
        "M7.7 8 A2.3 2.3 0 1 0 12.3 8 A2.3 2.3 0 1 0 7.7 8 Z " +
        "M12.3 16 A2.3 2.3 0 1 0 16.9 16 A2.3 2.3 0 1 0 12.3 16 Z";

    /// <summary>关于（信息）。</summary>
    public const string About =
        "M12 3.6 A8.4 8.4 0 1 0 12 20.4 A8.4 8.4 0 1 0 12 3.6 Z M12 11.2 V16.6 M12 7.9 H12.04";

    /// <summary>刷新。</summary>
    public const string Refresh =
        "M19.6 12 A7.6 7.6 0 1 1 16.7 6.1 M16.7 6.1 H20.2 M16.7 6.1 V2.6";

    /// <summary>新增。</summary>
    public const string Add = "M12 4.6 V19.4 M4.6 12 H19.4";

    /// <summary>删除（垃圾桶）。</summary>
    public const string Delete =
        "M5.4 7.4 H18.6 M9.4 7.4 V5.2 H14.6 V7.4 M6.9 7.4 V19.4 H17.1 V7.4 " +
        "M10.2 10.6 V16.4 M13.8 10.6 V16.4";

    /// <summary>文件夹。</summary>
    public const string Folder = "M3.6 6.6 H9.6 L11.4 9 H20.4 V18.4 H3.6 Z";

    /// <summary>恢复（逆时针回转）。</summary>
    public const string Restore =
        "M4.4 12 A7.6 7.6 0 1 0 7.3 6.1 M7.3 6.1 H3.8 M7.3 6.1 V2.6";

    /// <summary>展开箭头（下）。</summary>
    public const string ChevronDown = "M6.9 9.8 L12 14.9 L17.1 9.8";

    /// <summary>展示箭头（右）。</summary>
    public const string ChevronRight = "M9.8 6.9 L14.9 12 L9.8 17.1";

    /// <summary>执行（播放）。</summary>
    public const string Run = "M8.8 6.2 L17.6 12 L8.8 17.8 Z";

    /// <summary>校验 / 通过。</summary>
    public const string Check = "M5.5 12.5 L10 17 L18.5 7.5";

    /// <summary>关闭。</summary>
    public const string Close = "M6.5 6.5 L17.5 17.5 M17.5 6.5 L6.5 17.5";

    /// <summary>复制。</summary>
    public const string Copy = "M8.6 8.6 H19.4 V19.4 H8.6 Z M5.4 15.4 H4.6 V4.6 H15.4 V5.4";

    /// <summary>导出 / 下载。</summary>
    public const string Download = "M12 4 V15 M8 11 L12 15 L16 11 M4.6 19.4 H19.4";

    /// <summary>全部图标（名称 → 路径数据）。测试据此逐条校验语法与坐标范围。</summary>
    public static IReadOnlyList<(string Name, string Data)> All { get; } = new[]
    {
        (nameof(Home), Home),
        (nameof(Nodes), Nodes),
        (nameof(Shards), Shards),
        (nameof(Indices), Indices),
        (nameof(Metrics), Metrics),
        (nameof(Search), Search),
        (nameof(Snapshot), Snapshot),
        (nameof(Rest), Rest),
        (nameof(Sql), Sql),
        (nameof(Theme), Theme),
        (nameof(Settings), Settings),
        (nameof(About), About),
        (nameof(Refresh), Refresh),
        (nameof(Add), Add),
        (nameof(Delete), Delete),
        (nameof(Folder), Folder),
        (nameof(Restore), Restore),
        (nameof(ChevronDown), ChevronDown),
        (nameof(ChevronRight), ChevronRight),
        (nameof(Run), Run),
        (nameof(Check), Check),
        (nameof(Close), Close),
        (nameof(Copy), Copy),
        (nameof(Download), Download),
    };
}
