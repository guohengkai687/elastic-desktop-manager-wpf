using ElasticDesktopManager.Core.Json;
using ElasticDesktopManager.Core.Models;

namespace ElasticDesktopManager.Core.Services;

/// <summary>应用设置持久化（JSON 文件，替代 es_setting 表）。</summary>
public class SettingService
{
    private static readonly object FileLock = new();
    private readonly string _file;

    public SettingService(string? file = null) => _file = file ?? AppPaths.SettingFile;

    public SettingProperty Load()
    {
        lock (FileLock)
        {
            string? json = AtomicFile.ReadText(_file);
            if (json is not null)
            {
                try
                {
                    var loaded = JsonHelper.Deserialize<SettingProperty>(json);
                    if (loaded != null)
                    {
                        EnsureValid(loaded);
                        return loaded;
                    }
                }
                catch (Exception)
                {
                    AtomicFile.Backup(_file);
                }
            }
            var def = new SettingProperty();
            Save(def);
            return def;
        }
    }

    public void Save(SettingProperty setting)
        => AtomicFile.WriteAllText(_file, JsonHelper.Serialize(setting));

    private static void EnsureValid(SettingProperty s)
    {
        if (s.Theme is not ("light" or "dark")) s.Theme = "light";
        if (s.Language is not ("zh_CN" or "en")) s.Language = "zh_CN";
        if (s.Timeout <= 0) s.Timeout = 60;
        if (s.CloseBehavior is not ("ask" or "minimize" or "exit") && !string.IsNullOrEmpty(s.CloseBehavior))
            s.CloseBehavior = "ask";
    }
}