using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class SearchView : UserControl
{
    private SearchViewModel? _vm;
    private string? _pendingIndex;

    public SearchView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _vm = DataContext as SearchViewModel;
            Localize();
            if (_vm is not null)
            {
                _vm.StructureChanged += RebuildColumns;
                if (!string.IsNullOrEmpty(_pendingIndex))
                {
                    _vm.PreselectIndex(_pendingIndex);
                    _pendingIndex = null;
                }
            }
        };
        Unloaded += (_, _) =>
        {
            if (_vm is not null)
                _vm.StructureChanged -= RebuildColumns;
        };
    }

    /// <summary>从索引页跳转带入预选索引；视图未加载完成时缓存到 Loaded 后应用。</summary>
    public void PreselectIndex(string indexName)
    {
        if (_vm is not null)
        {
            _vm.PreselectIndex(indexName);
        }
        else
        {
            _pendingIndex = indexName;
        }
    }

    private void Localize()
    {
        TitleText.Text = Localization.L("nav.search");
        IndexLabel.Text = Localization.L("search.index");
        TimeoutLabel.Text = Localization.L("search.timeout");
        TrackCheck.Content = Localization.L("search.trackTotal");
        UpdateButton.Content = Localization.L("search.update");
        DeleteButton.Content = Localization.L("search.delete");
        DslButton.Content = Localization.L("search.showQuery");
        RunButton.Content = Localization.L("search.run");
        BuilderTitle.Text = Localization.L("search.builder.empty");
        AddConditionButton.Content = "+ " + Localization.L("search.addCondition");
        ScriptLabel.Text = Localization.L("search.script");
        TabTableHeader.Text = Localization.L("search.tab.table");
        TabJsonHeader.Text = Localization.L("search.tab.json");
        NoDataHint.Text = Localization.L("search.noData");
    }

    private void RebuildColumns()
    {
        if (_vm is null) return;
        ResultGrid.Columns.Clear();
        foreach (var colName in _vm.Columns)
        {
            ResultGrid.Columns.Add(new DataGridTextColumn
            {
                Header = colName,
                Binding = new Binding($"[{colName}]"),
                Width = DataGridLength.SizeToHeader,
            });
        }
    }

    private async void OnRun(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) await _vm.RunCommand.ExecuteAsync(null);
    }

    private void OnShowDsl(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.ShowDslCommand.Execute(null);
    }

    private void OnAddCondition(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.AddConditionCommand.Execute(null);
    }

    private async void OnUpdate(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) await _vm.UpdateByQueryCommand.ExecuteAsync(null);
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) await _vm.DeleteByQueryCommand.ExecuteAsync(null);
    }
}