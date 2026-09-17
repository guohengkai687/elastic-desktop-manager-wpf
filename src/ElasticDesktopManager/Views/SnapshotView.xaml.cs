using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ElasticDesktopManager.Core.I18n;

namespace ElasticDesktopManager.Views;

/// <summary>
/// 快照页五个列表（仓库 / 快照 / 恢复 / 自动策略 / 生命周期）的本地化。
/// 文本统一在这里赋值，XAML 里保留英文占位，便于比对与维护。
/// </summary>
public partial class SnapshotView : UserControl
{
    private static readonly (int Index, string Key)[] RepoHeaders =
    {
        (0, "snapshot.col.name"), (1, "snapshot.col.type"), (2, "snapshot.col.location"),
    };

    private static readonly (int Index, string Key)[] SnapshotHeaders =
    {
        (0, "snapshot.col.snapshot"), (1, "snapshot.col.state"), (2, "snapshot.col.indexCount"),
        (3, "snapshot.col.shards"), (4, "snapshot.col.started"), (5, "snapshot.col.duration"),
        (6, "snapshot.col.version"),
    };

    private static readonly (int Index, string Key)[] RecoveryHeaders =
    {
        (0, "snapshot.col.index"), (1, "snapshot.col.shard"), (2, "snapshot.col.stage"),
        (3, "snapshot.col.type"), (4, "snapshot.col.source"), (5, "snapshot.col.target"),
        (6, "snapshot.col.files"), (7, "snapshot.col.bytes"), (8, "snapshot.col.time"),
    };

    private static readonly (int Index, string Key)[] SlmHeaders =
    {
        (0, "snapshot.col.policyId"), (1, "snapshot.col.schedule"), (2, "snapshot.col.repository"),
        (3, "snapshot.col.nameTemplate"), (4, "snapshot.col.nextExecution"), (5, "snapshot.col.lastSuccess"),
        (6, "snapshot.col.lastFailure"), (7, "snapshot.col.stats"),
    };

    private static readonly (int Index, string Key)[] IlmHeaders =
    {
        (0, "snapshot.col.policyId"), (1, "snapshot.col.phases"), (2, "snapshot.col.inUseCount"),
        (3, "snapshot.col.modified"), (4, "snapshot.col.inUse"),
    };

    public SnapshotView()
    {
        InitializeComponent();
        Loaded += (_, _) => Localize();
    }

    private void Localize()
    {
        TitleText.Text = Localization.L("nav.snapshot");
        SubtitleText.Text = Localization.L("snapshot.subtitle");

        TabRepo.Header = Localization.L("snapshot.tab.repo");
        TabSnapshots.Header = Localization.L("snapshot.tab.snapshots");
        TabRestore.Header = Localization.L("snapshot.tab.restore");
        TabSlm.Header = Localization.L("snapshot.tab.slm");
        TabIlm.Header = Localization.L("snapshot.tab.ilm");

        // ① 仓库
        RepoNewText.Text = Localization.L("snapshot.repo.create");
        RepoTip(RepoVerifyButton, "snapshot.repo.verify");
        RepoTip(RepoDeleteButton, "snapshot.repo.delete");
        RepoTip(RepoRefreshButton, "common.refresh");
        RepoFormTitle.Text = Localization.L("snapshot.repo.create");
        RepoTypeLabel.Text = Localization.L("snapshot.repo.type");
        RepoNameLabel.Text = Localization.L("snapshot.col.name");
        RepoLocationLabel.Text = Localization.L("snapshot.repo.location");
        RepoCreateButton.Content = Localization.L("common.confirm");
        RepoPathHint.Text = Localization.L("snapshot.repo.pathHint");
        RepoSettingsLabel.Text = Localization.L("snapshot.settings");
        ApplyHeaders(RepoGrid, RepoHeaders);

        // ② 快照
        SnapshotNewText.Text = Localization.L("snapshot.create");
        RepoTip(SnapshotJsonButton, "snapshot.showJson");
        RepoTip(SnapshotDeleteButton, "snapshot.delete");
        RepoTip(SnapshotRefreshButton, "common.refresh");
        SnapshotFormTitle.Text = Localization.L("snapshot.create");
        SnapshotNameLabel.Text = Localization.L("snapshot.create.name");
        SnapshotIndicesLabel.Text = Localization.L("snapshot.create.indices");
        SnapshotGlobalStateCheck.Content = Localization.L("snapshot.create.globalState");
        SnapshotCreateButton.Content = Localization.L("common.confirm");
        SnapshotAsyncHint.Text = Localization.L("snapshot.create.asyncHint");
        ApplyHeaders(SnapshotGrid, SnapshotHeaders);

        // ③ 恢复
        RestoreRunButton.Content = Localization.L("snapshot.restore.run");
        RepoTip(RestoreRefreshButton, "snapshot.restore.refresh");
        RestoreRepoLabel.Text = Localization.L("snapshot.col.repository");
        RestoreSnapshotLabel.Text = Localization.L("snapshot.col.snapshot");
        RestoreIndicesLabel.Text = Localization.L("snapshot.restore.indices");
        RenamePatternLabel.Text = Localization.L("snapshot.restore.renamePattern");
        RenameReplacementLabel.Text = Localization.L("snapshot.restore.renameReplacement");
        RestoreGlobalStateCheck.Content = Localization.L("snapshot.create.globalState");
        RestoreWarning.Text = Localization.L("snapshot.restore.warning");
        ApplyHeaders(RecoveryGrid, RecoveryHeaders);

        // ④ 自动策略
        SlmNewText.Text = Localization.L("snapshot.slm.create");
        RepoTip(SlmExecuteButton, "snapshot.slm.execute");
        RepoTip(SlmJsonButton, "snapshot.showJson");
        RepoTip(SlmDeleteButton, "snapshot.slm.delete");
        RepoTip(SlmRefreshButton, "common.refresh");
        SlmFormTitle.Text = Localization.L("snapshot.slm.create");
        SlmIdLabel.Text = Localization.L("snapshot.col.policyId");
        SlmNameLabel.Text = Localization.L("snapshot.col.nameTemplate");
        SlmScheduleLabel.Text = Localization.L("snapshot.col.schedule");
        SlmRepoLabel.Text = Localization.L("snapshot.col.repository");
        SlmIndicesLabel.Text = Localization.L("snapshot.create.indices");
        SlmExpireLabel.Text = Localization.L("snapshot.slm.expireAfter");
        SlmMinLabel.Text = Localization.L("snapshot.slm.minCount");
        SlmMaxLabel.Text = Localization.L("snapshot.slm.maxCount");
        SlmCreateButton.Content = Localization.L("common.confirm");
        SlmPolicyJsonLabel.Text = Localization.L("snapshot.slm.policyJson");
        ApplyHeaders(SlmGrid, SlmHeaders);

        // ⑤ 生命周期
        IlmNewText.Text = Localization.L("snapshot.ilm.create");
        RepoTip(IlmJsonButton, "snapshot.showJson");
        RepoTip(IlmDeleteButton, "snapshot.ilm.delete");
        RepoTip(IlmRefreshButton, "common.refresh");
        IlmFormTitle.Text = Localization.L("snapshot.ilm.create");
        IlmIdLabel.Text = Localization.L("snapshot.col.policyId");
        IlmTemplateButton.Content = Localization.L("snapshot.ilm.fillTemplate");
        IlmBodyHint.Text = Localization.L("snapshot.ilm.bodyHint");
        IlmCreateButton.Content = Localization.L("common.confirm");
        IlmJsonLabel.Text = Localization.L("snapshot.ilm.policyJson");
        ApplyHeaders(IlmGrid, IlmHeaders);
    }

    private static void RepoTip(ButtonBase button, string key) => button.ToolTip = Localization.L(key);

    private static void ApplyHeaders(DataGrid grid, (int Index, string Key)[] map)
    {
        foreach (var (index, key) in map)
            if (index < grid.Columns.Count)
                grid.Columns[index].Header = Localization.L(key);
    }
}
