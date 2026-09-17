using System.Windows;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Core.Services;
using ElasticDesktopManager.Services;

namespace ElasticDesktopManager.Views;

public partial class FolderFormDialog : Window
{
    private readonly ConfigProperty _item;
    private readonly ConfigService _service = App.ConfigService;

    public FolderFormDialog(Window owner, ConfigProperty item)
    {
        InitializeComponent();
        Owner = owner;
        _item = item;

        bool isNew = string.IsNullOrEmpty(item.Id);
        Title = Localization.L(isNew ? "config.form.folder.new" : "config.form.folder.edit");
        NameLabel.Text = Localization.L("config.form.name");
        SaveButton.Content = Localization.L(isNew ? "config.addFolder" : "common.ok");
        CancelButton.Content = Localization.L("common.cancel");
        NameBox.Text = item.Name;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            Ui.Error(this, Localization.L("validate.required", Localization.L("config.form.name")));
            return;
        }
        _item.Name = name;
        _item.Type = "folder";
        _service.Upsert(_item);
        Ui.Toast(Localization.L("config.save.success"));
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}