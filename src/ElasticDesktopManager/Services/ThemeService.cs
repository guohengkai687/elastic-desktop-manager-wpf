using System.Windows;
using System.Windows.Media;

namespace ElasticDesktopManager.Services;

/// <summary>主题切换：替换 App 资源中的 Light/Dark 词典。</summary>
public static class ThemeService
{
    private const string Light = "Themes/Light.xaml";
    private const string Dark = "Themes/Dark.xaml";

    public static string Current { get; private set; } = "light";

    public static void ApplyTheme(string theme)
    {
        theme = theme is "dark" ? "dark" : "light";
        if (Current == theme) return;
        if (Application.Current is null) return;

        var dicts = Application.Current.Resources.MergedDictionaries;
        var uri = new Uri(theme == "dark" ? Dark : Light, UriKind.Relative);
        var next = new ResourceDictionary { Source = uri };
        dicts.Add(next);
        dicts.RemoveAt(1); // 索引 0 = Common.xaml
        Current = theme;
    }
}