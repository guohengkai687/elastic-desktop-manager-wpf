using System.Text;
using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Json;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.Services;

namespace ElasticDesktopManager.ViewModels;

public class SqlViewModel : PageViewModelBase
{
    public List<int> FetchSizes { get; } = new() { 50, 100, 200, 500, 1000 };

    private string _queryText = "";
    public string QueryText
    {
        get => _queryText;
        set => SetProperty(ref _queryText, value ?? "");
    }

    private int _fetchSize = 100;
    public int FetchSize
    {
        get => _fetchSize;
        set => SetProperty(ref _fetchSize, value);
    }

    public ObservableList<Dictionary<string, string>> Rows { get; } = new();
    public List<string> Columns { get; private set; } = new();

    private string _rawJson = "";
    public string RawJson
    {
        get => _rawJson;
        private set => SetProperty(ref _rawJson, value);
    }

    private string _summaryText = "";
    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    private int _pageIndex;
    public int PageIndex
    {
        get => _pageIndex;
        private set
        {
            if (SetProperty(ref _pageIndex, value))
            {
                OnPropertyChanged(nameof(PageInfoText));
                OnPropertyChanged(nameof(CanPrev));
                OnPropertyChanged(nameof(CanNext));
            }
        }
    }

    public bool CanPrev => PageIndex > 1;
    public bool CanNext => _current is { HasCursor: true };
    public string PageInfoText => Localization.L("sql.page", Math.Max(PageIndex, 1));

    private long _totalRows;
    private EsSqlResult? _current;
    private readonly List<EsSqlResult> _history = new();

    public AsyncRelayCommand ExecuteCommand { get; }
    public AsyncRelayCommand NextCommand { get; }
    public ICommand PrevCommand { get; }
    public ICommand ExportCommand { get; }

    /// <summary>列表结构变化（列集合）后通知视图重建 DataGrid 列。</summary>
    public event Action? StructureChanged;

    public SqlViewModel()
    {
        ExecuteCommand = new AsyncRelayCommand(_ => ExecuteAsync());
        NextCommand = new AsyncRelayCommand(_ => NextAsync());
        PrevCommand = new RelayCommand(_ => Prev());
        ExportCommand = new RelayCommand(_ => ExportCsv());
    }

    public override Task ReloadAsync()
    {
        // 不自动重跑 SQL
        return Task.CompletedTask;
    }

    private async Task ExecuteAsync()
    {
        if (!RequireConnection()) return;
        if (string.IsNullOrWhiteSpace(QueryText))
        {
            Ui.Error(null, Localization.L("validate.required", Localization.L("sql.title")));
            return;
        }

        await RunAsync(async () =>
        {
            // 关闭上一个查询遗留的游标，释放服务端资源（与源项目一致）
            TryCloseCursor(_current);

            _history.Clear();
            string raw = await Client.ExecuteSqlAsync(QueryText.Trim(), FetchSize);
            ApplyResult(raw, reset: true);
        });
    }

    private async Task NextAsync()
    {
        if (!RequireConnection() || _current is not { HasCursor: true }) return;
        await RunAsync(async () =>
        {
            string raw = await Client.ExecuteNextSqlAsync(_current!.Cursor!);
            ApplyResult(raw, reset: false);
        });
    }

    private void Prev()
    {
        if (_history.Count <= 1) return;
        var popped = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        _current = _history[^1];
        PageIndex--;
        // 被弹掉的页若持有游标则关闭（下一批数据不再需要）
        TryCloseCursor(popped);
        RenderFromCurrent();
    }

    /// <summary>静默关闭游标（尽力而为，失败不影响主流程）。</summary>
    private void TryCloseCursor(EsSqlResult? result)
    {
        if (result is not { HasCursor: true } || !HasConnection) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Client.CloseSqlAsync(result.Cursor!);
            }
            catch (Exception)
            {
                // 游标可能已过期，忽略
            }
        });
    }

    private void ApplyResult(string raw, bool reset)
    {
        var parsed = EsParsers.ParseSqlResult(raw);
        RawJson = JsonHelper.Pretty(raw);

        if (reset)
        {
            _history.Clear();
            _totalRows = 0;
            PageIndex = 0;
        }
        _history.Add(parsed);
        _current = parsed;
        PageIndex++;
        _totalRows += parsed.Rows.Count;
        RenderFromCurrent();
    }

    private void RenderFromCurrent()
    {
        if (_current is null) return;

        Columns = _current.Columns;
        var rows = new List<Dictionary<string, string>>();
        foreach (var row in _current.Rows)
        {
            var dict = new Dictionary<string, string>();
            for (int i = 0; i < Columns.Count; i++)
                dict[Columns[i]] = FormatCell(row.Count > i ? row[i] : null);
            rows.Add(dict);
        }
        Rows.ReplaceAll(rows);

        string cursorTip = _current.HasCursor ? " · cursor" : "";
        SummaryText = $"{Localization.L("sql.took")}: {_current.Took}ms  ·  {Localization.L("sql.rows", _totalRows)}{cursorTip}  ·  {PageInfoText}";
        StructureChanged?.Invoke();
    }

    private static string FormatCell(object? value)
    {
        return value switch
        {
            null => "",
            string s => s,
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? "",
        };
    }

    private void ExportCsv()
    {
        if (Columns.Count == 0 || Rows.Count == 0)
        {
            Ui.Toast(Localization.L("sql.noData"));
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = Localization.L("sql.csv"),
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"es-sql-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Columns.Select(CsvEscape)));
            foreach (var row in Rows)
                sb.AppendLine(string.Join(",", Columns.Select(c => CsvEscape(row.TryGetValue(c, out var v) ? v : ""))));
            // UTF-8 带 BOM，Excel 打开中文不乱码
            File.WriteAllText(dlg.FileName, sb.ToString(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Ui.Toast(Localization.L("sql.export.success"));
        }
        catch (Exception ex)
        {
            Ui.Error(null, ex.Message);
        }
    }

    private static string CsvEscape(string v)
    {
        if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        return v;
    }
}