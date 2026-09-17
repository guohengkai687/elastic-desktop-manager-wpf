using System.Windows;
using Microsoft.Win32;

namespace ElasticDesktopManager.Services;

/// <summary>
/// 主题切换：只替换“颜色词典”（Light/Dark）。
/// 几何令牌（Tokens.xaml）与控件模板（Common.xaml）永不触碰。
///
/// 历史缺陷（已修，用户表现为“UI 变回旧样子 / 设计系统像没生效”）：
/// 旧实现用 <c>dicts.RemoveAt(1)</c> 删除旧主题词典，并注释假定“索引 0 = Common.xaml”。
/// 后来在 Common 之前插入了 Tokens.xaml，索引整体后移一位，于是切主题时被删掉的其实是
/// <b>Common.xaml（整套控件模板）</b>：
///   · 所有隐式样式消失 → 控件回退成 WPF 默认灰白外观（所以“看起来跟没改过一样”）；
///   · 此后按 StaticResource 新建的页面会因找不到 CardStyle 等而直接抛异常。
/// 现在按 <see cref="ResourceDictionary.Source"/> 识别主题词典，不再依赖任何索引。
/// （守卫亦有规则禁止对 MergedDictionaries 使用字面量下标。）
/// </summary>
public static class ThemeService
{
    private const string LightFile = "Themes/Light.xaml";
    private const string DarkFile = "Themes/Dark.xaml";
    private static readonly string[] ThemeFiles = { LightFile, DarkFile };

    /// <summary>当前实际生效的主题：light / dark。</summary>
    public static string Current { get; private set; } = "light";

    /// <summary>当前是否处于“跟随系统深浅色”模式。</summary>
    public static bool FollowSystem { get; private set; }

    /// <summary>主题实际发生变化时触发（供 UI 同步主题图标等）。</summary>
    public static event Action? ThemeChanged;

    /// <summary>
    /// 应用主题。<paramref name="followSystem"/> 为 true 表示“跟随系统”，
    /// 会订阅 Windows 个性化设置变更并在系统切换深浅色时自动跟随。
    /// </summary>
    public static void ApplyTheme(string theme, bool followSystem = false)
    {
        FollowSystem = followSystem;
        SetFollowSystemWatch(followSystem);
        Apply(theme);
    }

    /// <summary>按当前设置解析并应用（启动时调用；跟随系统时读注册表）。</summary>
    public static void ApplyFromSettings()
    {
        var settings = App.Settings;
        var theme = settings.AutoTheme ? SystemTheme.GetWindowsTheme() : settings.Theme;
        ApplyTheme(theme, settings.AutoTheme);
    }

    private static void Apply(string theme)
    {
        bool wantDark = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase);
        string target = wantDark ? DarkFile : LightFile;
        string resolved = wantDark ? "dark" : "light";

        if (Application.Current is null)
        {
            Current = resolved;
            return;
        }

        var dicts = Application.Current.Resources.MergedDictionaries;
        // 快照一份再增删，避免边遍历边修改
        var installed = dicts.Where(IsThemeDictionary).ToList();
        var active = installed.FirstOrDefault(d => HasSource(d, target));

        if (active is null)
        {
            active = new ResourceDictionary { Source = new Uri(target, UriKind.Relative) };
            dicts.Add(active); // 追加到末尾：颜色令牌优先级最高（覆盖 App.xaml 里的默认 Light）
        }

        // 清掉其余主题词典（含历史遗留的重复项），保证同一时刻只有一套颜色生效
        foreach (var d in installed)
        {
            if (!ReferenceEquals(d, active))
                dicts.Remove(d);
        }

        bool changed = Current != resolved;
        Current = resolved;
        if (changed) ThemeChanged?.Invoke();
    }

    private static bool IsThemeDictionary(ResourceDictionary d)
        => d.Source is not null && ThemeFiles.Any(f => HasSource(d, f));

    private static bool HasSource(ResourceDictionary d, string file)
        => d.Source is not null &&
           string.Equals(d.Source.OriginalString, file, StringComparison.OrdinalIgnoreCase);

    // ---- 跟随系统 ----

    private static bool _watching;

    private static void SetFollowSystemWatch(bool on)
    {
        if (on == _watching) return;
        try
        {
            if (on) SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            else SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _watching = on;
        }
        catch (Exception)
        {
            // 非交互式会话等场景订阅可能失败；不影响手动主题切换
            _watching = false;
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!FollowSystem) return;
        var app = Application.Current;
        if (app is null) return;

        // SystemEvents 在专用线程上回调，必须切回 UI 线程
        app.Dispatcher.BeginInvoke(() =>
        {
            if (!FollowSystem) return;
            string want = SystemTheme.GetWindowsTheme();
            if (Current != want) Apply(want);
        });
    }
}
