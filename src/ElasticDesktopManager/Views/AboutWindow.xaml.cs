using System.Diagnostics;
using System.Windows;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Services;

namespace ElasticDesktopManager.Views;

public partial class AboutWindow : Window
{
    /// <summary>本项目仓库地址（「GitHub」按钮打开的目标）。</summary>
    private const string RepoUrl = "https://github.com/guohengkai687/elastic-desktop-manager-wpf";

    public AboutWindow()
    {
        InitializeComponent();
        Owner = Ui.Main;

        AppTitle.Text = Localization.L("app.title");
        VersionText.Text = Localization.L("about.version", "1.0.0-wpf");
        AuthorText.Text = Localization.L("about.author", Localization.L("app.author"));
        DescText.Text = Localization.L("about.desc");
        TechText.Text = Localization.L("about.tech");
        GithubButton.Content = Localization.L("about.github");
        CloseButton.Content = Localization.L("common.close");
        Title = Localization.L("about.title");
    }

    private void OnGithub(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepoUrl)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Ui.Error(this, ex.Message);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}