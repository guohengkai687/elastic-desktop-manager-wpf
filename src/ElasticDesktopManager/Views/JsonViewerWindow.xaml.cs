using System.Windows;
using ElasticDesktopManager.Core.I18n;

namespace ElasticDesktopManager.Views;

public partial class JsonViewerWindow : Window
{
    public JsonViewerWindow(string title, string json)
    {
        InitializeComponent();
        Title = title;
        JsonBox.Text = json;
        CopyButton.Content = "⧉ " + Localization.L("common.copy");
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(JsonBox.Text);
            ElasticDesktopManager.Services.Ui.Toast(Localization.L("common.action.success"));
        }
        catch (Exception)
        {
            // 剪贴板被占用时忽略
        }
    }
}