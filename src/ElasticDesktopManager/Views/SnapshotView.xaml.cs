using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class SnapshotView : UserControl
{
    public SnapshotView()
    {
        InitializeComponent();
        ApplyTexts();
        // 视图由 MainViewModel 缓存、生命周期与应用一致，因此订阅不需解绑
        Localization.LanguageChanged += ApplyTexts;
    }

    private void ApplyTexts()
    {
        TitleText.Text = Localization.L("nav.snapshot");

        RepoSectionTitle.Text = Localization.L("snapshot.repo");
        RepoNameLabel.Text = Localization.L("snapshot.repo.name");
        RepoLocationLabel.Text = Localization.L("snapshot.repo.location");
        RepoPathHint.Text = Localization.L("snapshot.repo.pathHint");
        CreateRepoButton.Content = Localization.L("snapshot.repo.create");
        VerifyRepoButton.Content = Localization.L("snapshot.repo.verify");
        DeleteRepoButton.Content = Localization.L("snapshot.repo.delete");
        RepoSettingsBox.ToolTip = Localization.L("snapshot.repo");

        CreateSnapshotTitle.Text = Localization.L("snapshot.create");
        SnapshotNameLabel.Text = Localization.L("snapshot.create.name");
        SnapshotIndicesLabel.Text = Localization.L("snapshot.create.indices");
        GlobalStateCheck.Content = Localization.L("snapshot.create.globalState");
        CreateSnapshotButton.Content = Localization.L("snapshot.create");

        DeleteSnapshotButton.Content = Localization.L("snapshot.delete");
        RestoreSnapshotButton.Content = Localization.L("snapshot.restore");
        RestoreIndicesBox.ToolTip = Localization.L("snapshot.restore.indices");

        ColName.Header = Localization.L("snapshot.col.name");
        ColState.Header = Localization.L("snapshot.col.state");
        ColIndices.Header = Localization.L("snapshot.col.indices");
        ColShards.Header = Localization.L("nav.shards");
        ColStarted.Header = Localization.L("snapshot.col.started");
        ColDuration.Header = Localization.L("snapshot.col.duration");
        ColVersion.Header = Localization.L("snapshot.col.version");
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (DataContext is SnapshotViewModel vm)
            await vm.ReloadAsync();
    }
}
