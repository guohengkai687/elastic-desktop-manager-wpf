using Microsoft.Win32;

namespace ElasticDesktopManager.Services;

/// <summary>读取 Windows 系统深浅色偏好（HKCU Themes\Personalize）。</summary>
public static class SystemTheme
{
    public static string GetWindowsTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i)
                return i == 1 ? "light" : "dark";
        }
        catch (Exception)
        {
            // 读不到时回退浅色
        }
        return "light";
    }
}