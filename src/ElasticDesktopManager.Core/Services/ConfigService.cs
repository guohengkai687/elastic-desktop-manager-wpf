using ElasticDesktopManager.Core.Json;
using ElasticDesktopManager.Core.Models;

namespace ElasticDesktopManager.Core.Services;

/// <summary>连接配置持久化（JSON 文件，替代 SQLite 的 es_config 表）。</summary>
public class ConfigService
{
    private static readonly object FileLock = new();
    private readonly string _file;

    public ConfigService(string? file = null) => _file = file ?? AppPaths.ConfigFile;

    public List<ConfigProperty> Load()
    {
        lock (FileLock)
        {
            string? json = AtomicFile.ReadText(_file);
            if (json is null)
            {
                Save(new List<ConfigProperty>());
                return new List<ConfigProperty>();
            }

            try
            {
                return JsonHelper.Deserialize<List<ConfigProperty>>(json) ?? new List<ConfigProperty>();
            }
            catch (Exception)
            {
                // JSON 损坏：备份留底，返回空表（不再静默覆盖丢失数据）
                AtomicFile.Backup(_file);
                return new List<ConfigProperty>();
            }
        }
    }

    public void Save(List<ConfigProperty> items)
        => AtomicFile.WriteAllText(_file, JsonHelper.Serialize(items));

    /// <summary>新增或更新（有 Id 视为更新）。</summary>
    public void Upsert(ConfigProperty item)
    {
        lock (FileLock)
        {
            var items = Load();
            if (string.IsNullOrEmpty(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("N");
                items.Add(item);
            }
            else
            {
                int idx = items.FindIndex(x => x.Id == item.Id);
                if (idx >= 0) items[idx] = item;
                else items.Add(item);
            }
            Save(items);
        }
    }

    /// <summary>删除，并级联删除子节点（与源项目 deleteById 一致）。</summary>
    public void DeleteCascade(string id)
    {
        lock (FileLock)
        {
            var items = Load();
            var toRemove = new List<ConfigProperty>();
            void Collect(ConfigProperty root)
            {
                toRemove.Add(root);
                foreach (var child in items.Where(x => x.ParentId == root.Id))
                    Collect(child);
            }
            var node = items.FirstOrDefault(x => x.Id == id);
            if (node != null)
            {
                Collect(node);
                Save(items.Where(x => !toRemove.Contains(x)).ToList());
            }
        }
    }

    /// <summary>重建整棵树（排序：文件夹在前，集群在后，按名称）。</summary>
    public List<ConfigProperty> LoadSorted() => Load()
        .OrderByDescending(x => x.IsFolder)
        .ThenBy(x => x.Name)
        .ToList();
}