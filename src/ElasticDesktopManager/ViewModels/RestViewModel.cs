using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Json;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.Services;

namespace ElasticDesktopManager.ViewModels;

public class RestViewModel : PageViewModelBase
{
    public List<string> Methods { get; } = new() { "GET", "POST", "PUT", "PATCH", "DELETE" };

    private string _method = "GET";
    public string Method
    {
        get => _method;
        set => SetProperty(ref _method, value ?? "GET");
    }

    private string _path = "/_cat/indices?v";
    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value ?? "");
    }

    private string _body = "";
    public string Body
    {
        get => _body;
        set => SetProperty(ref _body, value ?? "");
    }

    private string _responseText = "";
    public string ResponseText
    {
        get => _responseText;
        private set => SetProperty(ref _responseText, value);
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private bool _isExecuting;
    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (SetProperty(ref _isExecuting, value))
            {
                OnPropertyChanged(nameof(CanExecute));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool CanExecute => !IsExecuting;

    public AsyncRelayCommand ExecuteCommand { get; }
    public ICommand FormatCommand { get; }
    public ICommand OpenHistoryCommand { get; }

    public RestViewModel()
    {
        ExecuteCommand = new AsyncRelayCommand(_ => ExecuteAsync(), _ => CanExecute);
        FormatCommand = new RelayCommand(_ => FormatBody());
        OpenHistoryCommand = new RelayCommand(_ => Ui.ShowRestHistory());
    }

    public override async Task ReloadAsync()
    {
        // REST 页面无自动加载逻辑（保持用户输入）
        await Task.CompletedTask;
    }

    public void LoadFromHistory(CommandHistoryItem item)
    {
        if (!string.IsNullOrEmpty(item.Method)) Method = item.Method;
        if (!string.IsNullOrEmpty(item.Command)) Path = item.Command;
        Body = item.CommandValue ?? "";
    }

    private async Task ExecuteAsync()
    {
        if (!RequireConnection()) return;
        if (string.IsNullOrWhiteSpace(Path))
        {
            Ui.Error(null, Localization.L("validate.required", Localization.L("rest.path")));
            return;
        }

        IsExecuting = true;
        Ui.SetBusy(true);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            string body = Body;
            string raw = await Client.ExecuteRestAsync(Method, Path, string.IsNullOrWhiteSpace(body) ? null : body);
            sw.Stop();

            ResponseText = JsonHelper.Pretty(raw);
            StatusText = $"{Method} {Path}  ·  {sw.ElapsedMilliseconds} ms";
            SaveHistory(Method, Path, body);
        }
        catch (Exception ex)
        {
            sw.Stop();
            StatusText = $"{Method} {Path}  ·  {sw.ElapsedMilliseconds} ms";
            Ui.Error(null, ex is EsException e ? e.Message : ex.Message);
        }
        finally
        {
            IsExecuting = false;
            Ui.SetBusy(false);
        }
    }

    private void SaveHistory(string method, string path, string body)
    {
        try
        {
            App.HistoryService.Add(new CommandHistoryItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Method = method,
                Command = path,
                CommandValue = string.IsNullOrWhiteSpace(body) ? null : body,
                CreateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            });
        }
        catch (Exception)
        {
            // 历史保存失败不影响主流程
        }
    }

    private void FormatBody()
    {
        if (string.IsNullOrWhiteSpace(Body)) return;
        Body = JsonHelper.Pretty(Body);
    }
}