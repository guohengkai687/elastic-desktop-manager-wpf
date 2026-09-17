namespace ElasticDesktopManager.Core;

/// <summary>应用数据目录与文件路径（可用 EDM_DATA_DIR 覆盖，测试隔离用）。</summary>
public static class AppPaths
{
    public static string DataDir => _dataDir.Value;

    private static readonly Lazy<string> _dataDir = new(() =>
    {
        var env = Environment.GetEnvironmentVariable("EDM_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env);

        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ElasticDesktopManager");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".elastic-desktop-manager");
    });

    public static string ConfigFile => Path.Combine(DataDir, "config.json");
    public static string SettingFile => Path.Combine(DataDir, "settings.json");
    public static string HistoryFile => Path.Combine(DataDir, "history.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
    }
}