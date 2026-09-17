using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.Services;

namespace ElasticDesktopManager.ViewModels;

/// <summary>模板页：可组合索引模板与组件模板的查看 / 删除 / 详情。</summary>
public class TemplatesViewModel : PageViewModelBase
{
    public ObservableList<EsTemplate> IndexTemplates { get; } = new();
    public ObservableList<EsTemplate> ComponentTemplates { get; } = new();

    private EsTemplate? _selectedIndexTemplate;
    /// <summary>可写：DataGrid 选中项绑定需要 setter（只读属性绑 TwoWay 会运行时抛异常）。</summary>
    public EsTemplate? SelectedIndexTemplate
    {
        get => _selectedIndexTemplate;
        set
        {
            if (SetProperty(ref _selectedIndexTemplate, value))
                OnPropertyChanged(nameof(IndexTemplateJson));
        }
    }

    private EsTemplate? _selectedComponentTemplate;
    public EsTemplate? SelectedComponentTemplate
    {
        get => _selectedComponentTemplate;
        set
        {
            if (SetProperty(ref _selectedComponentTemplate, value))
                OnPropertyChanged(nameof(ComponentTemplateJson));
        }
    }

    public string IndexTemplateJson => SelectedIndexTemplate?.BodyJson ?? "";
    public string ComponentTemplateJson => SelectedComponentTemplate?.BodyJson ?? "";

    private string _summary = "";
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public ICommand DeleteIndexTemplateCommand { get; }
    public ICommand DeleteComponentTemplateCommand { get; }

    public TemplatesViewModel()
    {
        DeleteIndexTemplateCommand = new AsyncRelayCommand(_ => DeleteIndexTemplateAsync());
        // 组件模板删除走同一端点族：/_component_template/{name}
        DeleteComponentTemplateCommand = new AsyncRelayCommand(_ => DeleteComponentTemplateAsync());
    }

    public override Task ReloadAsync() => LoadAsync(busy: true, silent: false);

    public override Task AutoReloadAsync() => LoadAsync(busy: false, silent: true);

    private async Task LoadAsync(bool busy, bool silent)
    {
        if (!HasConnection) return;
        await RunAsync(async () =>
        {
            var indexTpl = EsParsers.ParseTemplates(await Client.GetIndexTemplatesAsync());
            var compTpl = EsParsers.ParseTemplates(await Client.GetComponentTemplatesAsync());

            IndexTemplates.ReplaceAll(indexTpl);
            ComponentTemplates.ReplaceAll(compTpl);

            if (SelectedIndexTemplate is null && indexTpl.Count > 0) SelectedIndexTemplate = indexTpl[0];
            if (SelectedComponentTemplate is null && compTpl.Count > 0) SelectedComponentTemplate = compTpl[0];

            Summary = $"{Localization.L("templates.index")}：{indexTpl.Count}   ·   " +
                      $"{Localization.L("templates.component")}：{compTpl.Count}";
        }, busy, silent);
    }

    private async Task DeleteIndexTemplateAsync()
    {
        if (!RequireConnection()) return;
        if (SelectedIndexTemplate is null) return;
        if (!Ui.Confirm(null, Localization.L("templates.delete.prompt", SelectedIndexTemplate.Name))) return;

        string name = SelectedIndexTemplate.Name;
        await RunAsync(async () =>
        {
            await Client.DeleteIndexTemplateAsync(name);
            Ui.Toast(Localization.L("templates.deleted", name));
            await LoadAsync(busy: false, silent: false);
        });
    }

    private async Task DeleteComponentTemplateAsync()
    {
        if (!RequireConnection()) return;
        if (SelectedComponentTemplate is null) return;
        if (!Ui.Confirm(null, Localization.L("templates.delete.prompt", SelectedComponentTemplate.Name))) return;

        string name = SelectedComponentTemplate.Name;
        await RunAsync(async () =>
        {
            await Client.DeleteComponentTemplateAsync(name);
            Ui.Toast(Localization.L("templates.deleted", name));
            await LoadAsync(busy: false, silent: false);
        });
    }
}
