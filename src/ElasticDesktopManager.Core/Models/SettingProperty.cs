namespace ElasticDesktopManager.Core.Models;

/// <summary>
/// 应用设置（源项目 SettingProperty 的等价物）。
/// </summary>
public class SettingProperty
{
    public string Id { get; set; } = "1";
    public string Language { get; set; } = "zh_CN";
    /// <summary>light / dark</summary>
    public string Theme { get; set; } = "light";
    /// <summary>请求超时（秒）。</summary>
    public int Timeout { get; set; } = 60;
    /// <summary>跟随系统外观自动切换主题。</summary>
    public bool AutoTheme { get; set; }
    /// <summary>启动时自动打开连接对话框。</summary>
    public bool OpenDialog { get; set; } = true;
    public string DownloadFolder { get; set; } = "";
    public bool AutoUpdater { get; set; }
    /// <summary>关闭行为：ask / minimize / exit</summary>
    public string CloseBehavior { get; set; } = "ask";
    public bool CloseRemember { get; set; }

    public int TimeoutMs => Math.Max(1000, Timeout * 1000);
    public int SqlTimeoutMs => Math.Max(1000, Timeout * 1000);

    public SettingProperty Clone() => (SettingProperty)MemberwiseClone();
}