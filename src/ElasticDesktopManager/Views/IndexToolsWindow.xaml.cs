using System.Windows;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Services;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

/// <summary>索引工具对话框：Mapping / Settings / 别名 / 字段 Top 值 / 维护 / 迁移。</summary>
public partial class IndexToolsWindow : Window
{
    private readonly IndexToolsViewModel _vm;

    public IndexToolsWindow(string indexName)
    {
        InitializeComponent();
        Owner = Ui.Main;

        _vm = new IndexToolsViewModel(indexName);
        DataContext = _vm;

        Title = $"{Localization.L("indextools.title")} - {indexName}";
        HeaderLabel.Text = Localization.L("indextools.title");
        CloseButton.Content = Localization.L("common.close");

        TabMapping.Header = Localization.L("indextools.mapping");
        TabSettings.Header = Localization.L("indextools.settings");
        TabAliases.Header = Localization.L("indextools.aliases");
        TabTopValues.Header = Localization.L("indextools.top");
        TabMaintenance.Header = Localization.L("indextools.maintenance");

        LoadMappingButton.Content = Localization.L("indextools.reload");
        SaveMappingButton.Content = Localization.L("common.save");
        LoadSettingsButton.Content = Localization.L("indextools.reload");
        SaveSettingsButton.Content = Localization.L("common.save");

        AliasNameLabel.Text = Localization.L("indextools.alias.name");
        AliasFilterLabel.Text = Localization.L("indextools.alias.filter");
        AliasRoutingLabel.Text = Localization.L("indextools.alias.routing");
        AddAliasButton.Content = Localization.L("common.add");
        RemoveAliasButton.Content = Localization.L("common.delete");

        ColAliasName.Header = Localization.L("indextools.alias.name");
        ColAliasIndex.Header = Localization.L("indextools.alias.index");
        ColAliasRouting.Header = Localization.L("indextools.alias.routing");
        ColAliasFilter.Header = Localization.L("indextools.alias.filter");

        TopFieldLabel.Text = Localization.L("indextools.top.field");
        TopSizeLabel.Text = Localization.L("indextools.top.size");
        TopKeywordCheck.Content = Localization.L("indextools.top.keyword");
        TopRunButton.Content = Localization.L("indextools.run");
        ColTopValue.Header = Localization.L("indextools.top.value");
        ColTopCount.Header = Localization.L("indextools.top.count");

        ForceMergeTitle.Text = Localization.L("indextools.forcemerge");
        ForceMergeHint.Text = Localization.L("indextools.forcemerge.hint");
        MaxSegmentsLabel.Text = Localization.L("indextools.forcemerge.maxSegments");
        ForceMergeButton.Content = Localization.L("indextools.run");

        ReindexTitle.Text = Localization.L("indextools.reindex");
        ReindexHint.Text = Localization.L("indextools.reindex.hint");
        ReindexTargetLabel.Text = Localization.L("indextools.reindex.target");
        ReindexQueryLabel.Text = Localization.L("indextools.reindex.query");
        ReindexButton.Content = Localization.L("indextools.run");

        Loaded += async (_, _) => await _vm.ReloadAsync();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
