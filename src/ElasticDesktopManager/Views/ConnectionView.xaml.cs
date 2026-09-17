using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class ConnectionView : Window
{
    private readonly ConnectionsViewModel _vm;

    public ConnectionView()
    {
        InitializeComponent();
        _vm = new ConnectionsViewModel(this);
        DataContext = _vm;

        FilterBox.ToolTip = Localization.L("config.filter.placeholder");
        FilterBox.Text = _vm.FilterText;
        OpenDialogCheck.Content = Localization.L("config.openDialog");
        AddClusterButton.Content = Localization.L("config.add");
        AddFolderButton.Content = Localization.L("config.addFolder");
        EditButton.Content = Localization.L("config.edit");
        DeleteButton.Content = Localization.L("config.delete");
        TestButton.Content = Localization.L("config.test");
        ConnectButton.Content = Localization.L("config.connect");
        CloseButton.Content = Localization.L("common.close");
        Title = Localization.L("config.title");
    }

    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        => _vm.Selected = e.NewValue as ConnectionTreeNode;

    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.Selected is { IsFolder: false })
            _vm.ConnectCommand.Execute(null);
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.Shutdown();
        base.OnClosed(e);
    }
}