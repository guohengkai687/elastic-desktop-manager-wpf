using System.Windows;
using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Core.Services;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.Services;
using ElasticDesktopManager.Views;

namespace ElasticDesktopManager.ViewModels;

/// <summary>连接树节点。</summary>
public class ConnectionTreeNode : ObservableObject
{
    public required ConfigProperty Item { get; init; }
    public ObservableList<ConnectionTreeNode> Children { get; } = new();

    public string Glyph => Item.IsFolder ? "📁" : "🖥";
    public string Name => Item.Name;
    public string? ServerText => Item.IsFolder ? null : $"{Item.Scheme}://{Item.Servers}";
    public string? Badge => Item.IsFolder ? null : (Item.SkipSslVerify ? "⚠ SSL" : (Item.Scheme == "https" ? "🔒" : null));
    public bool IsFolder => Item.IsFolder;
}

public class ConnectionsViewModel : ObservableObject
{
    private readonly ConfigService _service = App.ConfigService;
    private string _filterText = "";

    public ObservableList<ConnectionTreeNode> Nodes { get; } = new();

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!SetProperty(ref _filterText, value)) return;
            Reload();
        }
    }

    private bool _openDialogOnStartup;
    public bool OpenDialogOnStartup
    {
        get => _openDialogOnStartup;
        set
        {
            if (SetProperty(ref _openDialogOnStartup, value))
            {
                var s = App.Settings;
                s.OpenDialog = value;
                App.SettingsService.Save(s);
            }
        }
    }

    private ConnectionTreeNode? _selected;
    public ConnectionTreeNode? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => Selected is not null;
    public bool CanConnect => Selected is { IsFolder: false };

    public ICommand AddClusterCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand TestConnectCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand CloseCommand { get; }

    public ConnectionsViewModel(Window owner)
    {
        _owner = owner;
        AddClusterCommand = new RelayCommand(_ => AddCluster());
        AddFolderCommand = new RelayCommand(_ => AddFolder());
        EditCommand = new RelayCommand(_ => Edit());
        DeleteCommand = new RelayCommand(_ => Delete());
        TestConnectCommand = new AsyncRelayCommand(_ => RunTestAsync());
        ConnectCommand = new AsyncRelayCommand(_ => ConnectAsync());
        CloseCommand = new RelayCommand(_ => owner.Close());

        OpenDialogOnStartup = App.Settings.OpenDialog;
        Localization.LanguageChanged += Reload;
        Reload();
    }

    private readonly Window _owner;
    private bool _disposed;

    public void Shutdown()
    {
        if (_disposed) return;
        _disposed = true;
        Localization.LanguageChanged -= Reload;
    }

    public void Reload()
    {
        var all = _service.Load();
        string filter = _filterText?.Trim().ToLowerInvariant() ?? "";

        var roots = all.Where(x => string.IsNullOrEmpty(x.ParentId)).ToList();
        var nodes = roots.Select(r => Build(r, all, filter)).Where(n => n is not null).Cast<ConnectionTreeNode>().ToList();
        Nodes.ReplaceAll(nodes);
    }

    private static ConnectionTreeNode? Build(ConfigProperty item, List<ConfigProperty> all, string filter)
    {
        var node = new ConnectionTreeNode { Item = item };

        bool selfMatch = item.IsFolder || Match(item, filter);
        bool hasChildMatch = false;

        foreach (var child in all.Where(x => x.ParentId == item.Id))
        {
            var childNode = Build(child, all, filter);
            if (childNode is null) continue;
            node.Children.Add(childNode);
            hasChildMatch = true;
        }

        if (!item.IsFolder)
        {
            if (selfMatch) return node;
            return null;
        }
        // 文件夹：自身匹配或包含匹配子项
        return selfMatch || hasChildMatch ? node : null;
    }

    private static bool Match(ConfigProperty item, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        return item.Name.ToLowerInvariant().Contains(filter)
               || item.Servers.ToLowerInvariant().Contains(filter);
    }

    private void AddCluster()
    {
        var parentId = Selected?.Item.Id ?? "";
        var form = new ConnectionFormDialog(_owner, new ConfigProperty { Type = "cluster", ParentId = parentId });
        if (form.ShowDialog() == true)
            Reload();
    }

    private void AddFolder()
    {
        var parent = Selected?.Item;
        string parentId = parent is { IsFolder: true } ? parent.Id : "";
        var form = new FolderFormDialog(_owner, new ConfigProperty { Type = "folder", ParentId = parentId });
        if (form.ShowDialog() == true)
            Reload();
    }

    private void Edit()
    {
        if (Selected is null) return;
        var item = Selected.Item;
        if (item.IsFolder)
        {
            var form = new FolderFormDialog(_owner, item.Clone());
            if (form.ShowDialog() == true) Reload();
        }
        else
        {
            var form = new ConnectionFormDialog(_owner, item.Clone());
            if (form.ShowDialog() == true) Reload();
        }
    }

    private void Delete()
    {
        if (Selected is null) return;
        if (!Ui.Confirm(_owner, Localization.L("config.delete.prompt"))) return;
        var session = EsSession.Instance;
        if (session.CurrentConfig?.Id == Selected.Item.Id)
        {
            // 删除的是当前连接 → 联动断开
            session.Disconnect();
            Ui.NotifyConnectionChanged();
        }
        _service.DeleteCascade(Selected.Item.Id);
        Ui.Toast(Localization.L("config.delete.success"));
        Reload();
    }

    private bool _isTesting;
    public bool IsTesting
    {
        get => _isTesting;
        private set => SetProperty(ref _isTesting, value);
    }

    /// <summary>测试连接（后台线程执行，避免卡 UI）。</summary>
    public async Task RunTestAsync()
    {
        if (Selected is not { IsFolder: false } node) return;
        IsTesting = true;
        Ui.SetBusy(true);
        try
        {
            var (success, error) = await Task.Run(() =>
            {
                bool ok = EsSession.TryTest(node.Item, App.Settings.Timeout, App.Settings.Timeout, out string err);
                return (ok, err);
            });
            Ui.Info(_owner, success
                ? Localization.L("config.connect.success")
                : $"{Localization.L("config.connect.fail")}：{error}");
        }
        finally
        {
            IsTesting = false;
            Ui.SetBusy(false);
        }
    }

    private async Task ConnectAsync()
    {
        if (Selected is not { IsFolder: false } node) return;
        Ui.SetBusy(true);
        try
        {
            await Task.Run(() => EsSession.Instance.Connect(node.Item, App.Settings.Timeout, App.Settings.Timeout));
            Ui.Toast($"{Localization.L("config.connect.success")}：{node.Item.Name}");
            Ui.NotifyConnectionChanged();
            Selected = null;
            // 成功即关闭连接对话框（与源项目一致）
            _owner.Dispatcher.Invoke(() => _owner.Close());
        }
        catch (Exception ex)
        {
            Ui.Error(_owner, $"{Localization.L("config.connect.fail")}：{(ex is EsException e ? e.Message : ex.Message)}");
        }
        finally
        {
            Ui.SetBusy(false);
        }
    }
}