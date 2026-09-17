using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.Services;

namespace ElasticDesktopManager.ViewModels;

/// <summary>分词调试页：调用 _analyze 查看文本被切成哪些 token。</summary>
public class AnalyzeViewModel : PageViewModelBase
{
    /// <summary>常用内置分词器（用户也可自行输入自定义分词器名）。</summary>
    public ObservableList<string> CommonAnalyzers { get; } = new()
    {
        "standard", "simple", "whitespace", "keyword", "stop", "english", "ik_max_word", "ik_smart",
    };

    private string _indexName = "";
    /// <summary>索引名（可空：为空时走 /_analyze 用内置分词器）。</summary>
    public string IndexName
    {
        get => _indexName;
        set => SetProperty(ref _indexName, value ?? "");
    }

    private string _analyzer = "standard";
    public string Analyzer
    {
        get => _analyzer;
        set => SetProperty(ref _analyzer, value ?? "");
    }

    private string _inputText = "";
    public string InputText
    {
        get => _inputText;
        set => SetProperty(ref _inputText, value ?? "");
    }

    public ObservableList<AnalyzeToken> Tokens { get; } = new();

    private string _summary = "";
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public ICommand RunCommand { get; }

    public AnalyzeViewModel()
    {
        RunCommand = new AsyncRelayCommand(_ => RunAsyncInternal());
    }

    public override Task ReloadAsync() => Task.CompletedTask; // 用户驱动，不自动拉取

    private async Task RunAsyncInternal()
    {
        if (!RequireConnection()) return;
        if (string.IsNullOrWhiteSpace(InputText))
        {
            Ui.Error(null, Localization.L("analyze.needText"));
            return;
        }

        await RunAsync(async () =>
        {
            string index = IndexName.Trim();
            string analyzer = Analyzer.Trim();

            string json = string.IsNullOrEmpty(index)
                ? await Client.AnalyzeTextWithBuiltinAsync(InputText, string.IsNullOrEmpty(analyzer) ? null : analyzer)
                : await Client.AnalyzeTextAsync(index, InputText, null,
                    string.IsNullOrEmpty(analyzer) ? null : analyzer);

            var tokens = EsParsers.ParseAnalyzeTokens(json);
            Tokens.ReplaceAll(tokens);
            Summary = $"{Localization.L("analyze.tokens")}：{tokens.Count}";
        });
    }
}
