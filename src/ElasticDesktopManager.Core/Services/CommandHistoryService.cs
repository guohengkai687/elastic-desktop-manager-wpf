using ElasticDesktopManager.Core.Json;
using ElasticDesktopManager.Core.Models;

namespace ElasticDesktopManager.Core.Services;

/// <summary>REST 命令历史持久化（JSON 文件，替代 es_command_history 表，上限 100 条）。</summary>
public class CommandHistoryService
{
    public const int MaxCount = 100;
    private static readonly object FileLock = new();
    private readonly string _file;

    public CommandHistoryService(string? file = null) => _file = file ?? AppPaths.HistoryFile;

    public List<CommandHistoryItem> Load()
    {
        lock (FileLock)
        {
            string? json = AtomicFile.ReadText(_file);
            if (json is null) return new List<CommandHistoryItem>();
            try
            {
                return JsonHelper.Deserialize<List<CommandHistoryItem>>(json) ?? new List<CommandHistoryItem>();
            }
            catch (Exception)
            {
                AtomicFile.Backup(_file);
                return new List<CommandHistoryItem>();
            }
        }
    }

    public void Add(CommandHistoryItem item)
    {
        lock (FileLock)
        {
            var items = Load();
            items.Insert(0, item);
            if (items.Count > MaxCount)
                items.RemoveRange(MaxCount, items.Count - MaxCount);
            AtomicFile.WriteAllText(_file, JsonHelper.Serialize(items));
        }
    }

    public void Delete(string id)
    {
        lock (FileLock)
        {
            var items = Load().Where(x => x.Id != id).ToList();
            AtomicFile.WriteAllText(_file, JsonHelper.Serialize(items));
        }
    }
}