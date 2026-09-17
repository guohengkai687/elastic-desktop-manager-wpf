using System.Windows;
using ElasticDesktopManager.Core.Es;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Core.Services;
using ElasticDesktopManager.Services;

namespace ElasticDesktopManager.Views;

/// <summary>集群连接表单：地址 / 协议(http/https=SSL) / 认证 / 跳过 SSL 验证 / 测试连接。</summary>
public partial class ConnectionFormDialog : Window
{
    private readonly ConfigProperty _item;
    private readonly bool _isNew;
    private readonly ConfigService _service = App.ConfigService;

    public ConnectionFormDialog(Window owner, ConfigProperty item)
    {
        InitializeComponent();
        Owner = owner;
        _item = item;
        _isNew = string.IsNullOrEmpty(item.Id);

        Title = Localization.L(_isNew ? "config.form.new" : "config.form.edit");
        NameLabel.Text = Localization.L("config.form.name");
        ServersLabel.Text = Localization.L("config.form.servers");
        ServersHint.Text = Localization.L("config.form.servers.hint");
        SchemeLabel.Text = Localization.L("config.form.scheme");
        HttpRadio.Content = Localization.L("config.form.http");
        HttpsRadio.Content = Localization.L("config.form.https");
        SecurityCheck.Content = Localization.L("config.form.security");
        UsernameLabel.Text = Localization.L("config.form.username");
        PasswordLabel.Text = Localization.L("config.form.password");
        SkipSslCheck.Content = Localization.L("config.form.skipSsl");
        SkipSslDesc.Text = Localization.L("config.form.skipSsl.hint");
        SkipSslHint.Text = Localization.L("config.form.skipSsl.hint");
        TestButton.Content = Localization.L("config.test");
        SaveButton.Content = Localization.L(_isNew ? "config.add" : "common.ok");
        CancelButton.Content = Localization.L("common.cancel");

        LoadValues();
    }

    private void LoadValues()
    {
        NameBox.Text = _item.Name;
        ServersBox.Text = _item.Servers;
        if (_item.Scheme == "https") HttpsRadio.IsChecked = true;
        else HttpRadio.IsChecked = true;
        SecurityCheck.IsChecked = _item.Security || !string.IsNullOrEmpty(_item.Username);
        UsernameBox.Text = _item.Username;
        PasswordBox.Password = _item.Password;
        SkipSslCheck.IsChecked = _item.SkipSslVerify;
        OnSecurityChanged(null!, null!);
    }

    private void OnSecurityChanged(object sender, RoutedEventArgs e)
    {
        bool enabled = SecurityCheck.IsChecked == true;
        UsernameBox.IsEnabled = enabled;
        PasswordBox.IsEnabled = enabled;
    }

    private async void OnTest(object sender, RoutedEventArgs e)
    {
        if (!TryCollect(out var cfg, out string err))
        {
            Ui.Error(this, err);
            return;
        }
        TestButton.IsEnabled = false;
        try
        {
            var (ok, error) = await Task.Run(() =>
            {
                bool success = EsSession.TryTest(cfg, App.Settings.Timeout, App.Settings.Timeout, out string err2);
                return (success, err2);
            });
            Ui.Info(this, ok ? Localization.L("config.connect.success")
                             : $"{Localization.L("config.connect.fail")}：{error}");
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!TryCollect(out var cfg, out string err))
        {
            Ui.Error(this, err);
            return;
        }
        cfg.Id = _item.Id;
        _service.Upsert(cfg);
        Ui.Toast(Localization.L("config.save.success"));
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private bool TryCollect(out ConfigProperty cfg, out string error)
    {
        cfg = new ConfigProperty();
        error = "";

        string name = NameBox.Text.Trim();
        string servers = ServersBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) { error = Localization.L("validate.required", Localization.L("config.form.name")); return false; }
        if (string.IsNullOrEmpty(servers)) { error = Localization.L("validate.required", Localization.L("config.form.servers")); return false; }

        cfg.Name = name;
        cfg.Servers = servers;
        cfg.Scheme = HttpsRadio.IsChecked == true ? "https" : "http";
        cfg.Security = SecurityCheck.IsChecked == true;
        cfg.Username = cfg.Security ? UsernameBox.Text.Trim() : "";
        cfg.Password = cfg.Security ? PasswordBox.Password : "";
        cfg.SkipSslVerify = SkipSslCheck.IsChecked == true;
        cfg.Type = "cluster";
        cfg.ParentId = _item.ParentId;
        return true;
    }
}