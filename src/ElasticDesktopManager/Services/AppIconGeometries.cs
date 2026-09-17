using System.Windows.Media;
using ElasticDesktopManager.Core.Ui;

namespace ElasticDesktopManager.Services;

/// <summary>
/// 把 <see cref="AppIcons"/> 里的路径数据（Core 中的纯字符串）转成 WPF 可用的 <see cref="Geometry"/>。
///
/// 为什么不直接在 XAML 里写 <c>Data="{Binding SomeString}"</c>：
/// 字符串 → Geometry 依赖类型转换器，且失败的时机在运行期。这里用 Geometry.Parse
/// 解析一次并静态缓存，类型精确、无运行期转换。
///
/// 语法正确性由 Core 测试逐条校验（tests/ElasticDesktopManager.Tests「图标」用例，
/// 校验命令集合/M 开头/坐标范围/整圆弧成对），因此正常不会走到 catch 分支。
/// 但图标坏掉不该让整个应用起不来，故兜底为 Geometry.Empty。
/// </summary>
public static class AppIconGeometries
{
    public static Geometry Home { get; } = Parse(AppIcons.Home);
    public static Geometry Nodes { get; } = Parse(AppIcons.Nodes);
    public static Geometry Shards { get; } = Parse(AppIcons.Shards);
    public static Geometry Indices { get; } = Parse(AppIcons.Indices);
    public static Geometry Metrics { get; } = Parse(AppIcons.Metrics);
    public static Geometry Search { get; } = Parse(AppIcons.Search);
    public static Geometry Snapshot { get; } = Parse(AppIcons.Snapshot);
    public static Geometry Rest { get; } = Parse(AppIcons.Rest);
    public static Geometry Sql { get; } = Parse(AppIcons.Sql);
    public static Geometry Theme { get; } = Parse(AppIcons.Theme);
    public static Geometry Settings { get; } = Parse(AppIcons.Settings);
    public static Geometry About { get; } = Parse(AppIcons.About);
    public static Geometry Refresh { get; } = Parse(AppIcons.Refresh);
    public static Geometry Add { get; } = Parse(AppIcons.Add);
    public static Geometry Delete { get; } = Parse(AppIcons.Delete);
    public static Geometry Folder { get; } = Parse(AppIcons.Folder);
    public static Geometry Restore { get; } = Parse(AppIcons.Restore);
    public static Geometry ChevronDown { get; } = Parse(AppIcons.ChevronDown);
    public static Geometry ChevronRight { get; } = Parse(AppIcons.ChevronRight);
    public static Geometry Run { get; } = Parse(AppIcons.Run);
    public static Geometry Check { get; } = Parse(AppIcons.Check);
    public static Geometry Close { get; } = Parse(AppIcons.Close);
    public static Geometry Copy { get; } = Parse(AppIcons.Copy);
    public static Geometry Download { get; } = Parse(AppIcons.Download);

    private static Geometry Parse(string data)
    {
        try
        {
            return Geometry.Parse(data);
        }
        catch (FormatException)
        {
            return Geometry.Empty;
        }
    }
}
