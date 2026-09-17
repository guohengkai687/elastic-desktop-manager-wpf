using System.Windows;
using System.Windows.Controls;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.ViewModels;

namespace ElasticDesktopManager.Views;

public partial class AnalyzeView : UserControl
{
    public AnalyzeView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            TitleText.Text = Localization.L("nav.analyze");
            HintText.Text = Localization.L("analyze.hint");
            IndexLabel.Text = Localization.L("analyze.index");
            AnalyzerLabel.Text = Localization.L("analyze.analyzer");
            TextFieldLabel.Text = Localization.L("analyze.text");
            RunButton.Content = Localization.L("analyze.run");
            ResultLabel.Text = Localization.L("analyze.result");
            NoResultHint.Text = Localization.L("analyze.noResult");

            ColToken.Header = Localization.L("analyze.col.token");
            ColType.Header = Localization.L("analyze.col.type");
            ColPosition.Header = Localization.L("analyze.col.position");
            ColRange.Header = Localization.L("analyze.col.range");
        };
    }
}
