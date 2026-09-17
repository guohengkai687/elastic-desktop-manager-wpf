using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Json;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.Services;
using ElasticDesktopManager.Views;

namespace ElasticDesktopManager.ViewModels;

/// <summary>索引行（展示模型）。</summary>
public class IndexRow
{
    public required EsIndex Item { get; init; }
    public string Name => Item.Index;
    public string Health => Item.Health;
    public string Status => Item.Status;
    public string Uuid => Item.Uuid;
    public string PriRep => $"{Item.Pri}/{Item.Rep}";
    public string DocsCount => Item.DocsCount;
    public string StoreSize => Item.StoreSize;
    public string MemoryTotal => Item.MemoryTotal;

    public string CreatedText
    {
        get
        {
            if (long.TryParse(Item.CreationDate, out long ms) && ms > 0)
            {
                try { return DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"); }
                catch (Exception) { return Item.CreationDate; }
            }
            return Item.CreationDate;
        }
    }
}

public class IndicesViewModel : PageViewModelBase
{
    public ObservableList<IndexRow> Items { get; } = new();
    public List<int> PageSizes { get; } = new() { 20, 30, 40, 50, 100 };

    private List<EsIndex> _all = new();
    private List<EsIndex> _filtered = new();

    private string _filterText = "";
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
                ApplyFilter();
        }
    }

    private int _pageIndex = 1;
    public int PageIndex
    {
        get => _pageIndex;
        set
        {
            if (SetProperty(ref _pageIndex, value))
            {
                OnPropertyChanged(nameof(PageInfoText));
                OnPropertyChanged(nameof(CanPrev));
                OnPropertyChanged(nameof(CanNext));
                ApplyPage();
            }
        }
    }

    private int _pageSize = 20;
    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (SetProperty(ref _pageSize, value))
            {
                PageIndex = 1;
                ApplyPage();
            }
        }
    }

    private int _totalItems;
    public int TotalItems
    {
        get => _totalItems;
        private set
        {
            if (SetProperty(ref _totalItems, value))
                OnPropertyChanged(nameof(TotalText));
        }
    }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(_filtered.Count / (double)PageSize);
    public bool CanPrev => PageIndex > 1;
    public bool CanNext => PageIndex < TotalPages;
    public string PageInfoText => $"{Localization.L("sql.page", PageIndex)} / {Math.Max(TotalPages, 1)}";

    /// <summary>“总数：N”。XAML 里原来写的是 StringFormat=Total: {0}，英文前缀在中文界面不会变。</summary>
    public string TotalText => Localization.L("index.total", TotalItems);

    public ICommand PrevCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand DetailsCommand { get; }
    public ICommand StatsCommand { get; }
    public ICommand RefreshIndexCommand { get; }
    public ICommand FlushCommand { get; }
    public ICommand CleanCacheCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand CloseCommand { get; }

    public IndicesViewModel()
    {
        PrevCommand = new RelayCommand(_ => { if (CanPrev) PageIndex--; });
        NextCommand = new RelayCommand(_ => { if (CanNext) PageIndex++; });
        SearchCommand = new RelayCommand(p =>
        {
            var row = Param(p);
            if (row is not null) Ui.NavigateSearch(row.Name);
        });
        // 传 key 而不是译文：译文传进来后 RunAction/ShowJson 还会再 L() 一次，
        // L() 查不到就原样返回 —— 中文界面看着正常，切英文时弹框仍是中文。
        DetailsCommand = new AsyncRelayCommand(p => ShowJson(p, "index.detail", (c, name) => c.GetIndexDetailsAsync(name)));
        StatsCommand = new AsyncRelayCommand(p => ShowJson(p, "index.stats", (c, name) => c.GetIndexStatsAsync(name)));
        RefreshIndexCommand = CreateConfirm("index.confirm.refresh", (c, i) => c.RefreshIndexAsync(i.Name, CancellationToken.None));
        FlushCommand = CreateConfirm("index.confirm.flush", (c, i) => c.FlushIndexAsync(i.Name, CancellationToken.None));
        CleanCacheCommand = CreateConfirm("index.confirm.clean", (c, i) => c.ClearIndexCacheAsync(i.Name, CancellationToken.None));
        OpenCommand = CreateConfirm("index.confirm.open", (c, i) => c.OpenIndexAsync(i.Name, CancellationToken.None));
        CloseCommand = CreateConfirm("index.confirm.close", (c, i) => c.CloseIndexAsync(i.Name, CancellationToken.None));
    }

    /// <summary>语言切换：页码/总数都是每次现读词条的，重发通知即可。</summary>
    protected override void OnRelocalize()
    {
        OnPropertyChanged(nameof(PageInfoText));
        OnPropertyChanged(nameof(TotalText));
    }

    public override Task ReloadAsync() => LoadAsync(busy: true, silent: false);

    /// <summary>进入页面时自动刷新：不占全局忙碌条、失败不弹模态框。</summary>
    public override Task AutoReloadAsync() => LoadAsync(busy: false, silent: true);

    private async Task LoadAsync(bool busy, bool silent)
    {
        if (!HasConnection) return;
        await RunAsync(async () =>
        {
            string json = await Client.GetIndicesAsync(EsClient.IndicesFormat);
            _all = EsParsers.ParseIndices(json);
            ApplyFilter();
        }, busy, silent);
    }

    private void ApplyFilter()
    {
        string f = FilterText?.Trim().ToLowerInvariant() ?? "";
        _filtered = string.IsNullOrEmpty(f)
            ? _all
            : _all.Where(x => x.Index.ToLowerInvariant().Contains(f)
                              || x.Health.ToLowerInvariant().Contains(f)
                              || x.Status.ToLowerInvariant().Contains(f)).ToList();

        TotalItems = _filtered.Count;
        PageIndex = 1;
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(CanNext));
        ApplyPage();
    }

    private void ApplyPage()
    {
        int pageSize = Math.Max(1, PageSize);
        int from = (Math.Max(1, PageIndex) - 1) * pageSize;
        if (from >= _filtered.Count) from = Math.Max(0, _filtered.Count - pageSize);
        var page = _filtered.Skip(from).Take(pageSize)
            .Select(x => new IndexRow { Item = x }).ToList();
        Items.ReplaceAll(page);
        OnPropertyChanged(nameof(PageInfoText));
        OnPropertyChanged(nameof(CanPrev));
        OnPropertyChanged(nameof(CanNext));
    }

    private static IndexRow? Param(object? p) => p as IndexRow;

    private async Task ShowJson(object? p, string titleKey, Func<EsClient, string, Task<string>> fetch)
    {
        var row = Param(p);
        if (row is null || !RequireConnection()) return;
        await RunAsync(async () =>
        {
            string raw = await fetch(Client, row.Name);
            string pretty = JsonHelper.Pretty(raw);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                new JsonViewerWindow($"{Localization.L(titleKey)} ({row.Name})", pretty) { Owner = Ui.Main }.ShowDialog());
        });
    }

    private ICommand CreateConfirm(string confirmKey, Func<EsClient, IndexRow, Task<string>> act)
        => new AsyncRelayCommand(p => RunAction(p, confirmKey, act));

    private async Task RunAction(object? p, string confirmKey, Func<EsClient, IndexRow, Task<string>> act)
    {
        var row = Param(p);
        if (row is null || !RequireConnection()) return;
        if (!Ui.Confirm(null, $"{Localization.L(confirmKey)} [{row.Name}]?")) return;

        await RunAsync(async () =>
        {
            await act(Client, row);
            Ui.Toast(Localization.L("common.action.success"));
            await ReloadAsync();
        });
    }
}