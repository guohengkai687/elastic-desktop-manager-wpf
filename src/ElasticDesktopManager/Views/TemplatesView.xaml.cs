using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class TemplatesView : UserControl
{
    public TemplatesView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            TitleText.Text = Localization.L("nav.templates");

            TabIndexTemplates.Header = Localization.L("templates.index");
            TabComponentTemplates.Header = Localization.L("templates.component");

            DeleteIndexTplButton.Content = Localization.L("common.delete");
            DeleteComponentTplButton.Content = Localization.L("common.delete");

            ColIndexName.Header = Localization.L("templates.col.name");
            ColIndexPatterns.Header = Localization.L("templates.col.patterns");
            ColIndexPriority.Header = Localization.L("templates.col.priority");
            ColComponentName.Header = Localization.L("templates.col.name");
            ColComponentVersion.Header = Localization.L("templates.col.version");
        };
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (DataContext is TemplatesViewModel vm)
            await vm.ReloadAsync();
    }
}
