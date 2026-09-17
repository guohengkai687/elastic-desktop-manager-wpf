using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class SqlView : UserControl
{
    private SqlViewModel? _vm;

    public SqlView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _vm = DataContext as SqlViewModel;
            TitleText.Text = Localization.L("sql.title");
            InputLabel.Text = Localization.L("sql.title");
            FetchLabel.Text = Localization.L("sql.fetchSize");
            ExecuteButton.Content = Localization.L("sql.execute");
            ExportButton.Content = Localization.L("sql.csv");
            PrevButton.Content = "‹ " + Localization.L("sql.prevPage");
            NextButton.Content = Localization.L("sql.nextPage") + " ›";
            TabTableHeader.Text = Localization.L("sql.tab.table");
            TabJsonHeader.Text = Localization.L("sql.tab.json");
            NoDataHint.Text = Localization.L("sql.noData");

            if (_vm is not null)
                _vm.StructureChanged += RebuildColumns;
        };
        Unloaded += (_, _) =>
        {
            if (_vm is not null)
                _vm.StructureChanged -= RebuildColumns;
        };
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

    private async void OnExecute(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) await _vm.ExecuteCommand.ExecuteAsync(null);
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) await _vm.NextCommand.ExecuteAsync(null);
    }

    private void OnPrev(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.PrevCommand.Execute(null);
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (_vm is not null) _vm.ExportCommand.Execute(null);
    }
}