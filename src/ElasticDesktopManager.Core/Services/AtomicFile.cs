namespace ElasticDesktopManager.Core.Services;

/// <summary>
/// 小文件原子读写：临时文件 + 覆盖移动，读取损坏时备份为 .corrupt-<时间戳> 以免静默覆盖丢失用户数据。
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>读取文件；不存在返回 null；解析由调用方进行。文件损坏时先备份再返回原内容（由调用方决定如何回退）。</summary>
    public static string? ReadText(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception)
        {
            // 读取失败（如被占用/损坏）→ 备份后重试一次
            Backup(path);
            return File.ReadAllText(path);
        }
    }

    /// <summary>把疑似损坏的文件改名留底。</summary>
    public static void Backup(string path)
    {
        try
        {
            string backup = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            if (!File.Exists(backup))
                File.Move(path, backup);
        }
        catch (Exception)
        {
            // 备份失败不阻断主流程
        }
    }
}