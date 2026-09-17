using System.Windows;
using ElasticDesktopManager.Core.I18n;
using ElasticDesktopManager.Views;

namespace ElasticDesktopManager.Services;

/// <summary>UI 级静态服务：对话框、消息、忙碌指示、页面跳转、连接变更通知。</summary>
public static class Ui
{
    public static MainWindow? Main { get; set; }

    /// <summary>连接建立/断开后触发（页面据此重载）。</summary>
    public static event Action? ConnectionChanged;

    public static void NotifyConnectionChanged() => ConnectionChanged?.Invoke();

    // ---------- 对话框 ----------
    public static void ShowConnections() => new ConnectionView { Owner = Main }.ShowDialog();

    public static void ShowSettings() => new SettingsWindow { Owner = Main }.ShowDialog();

    public static void ShowAbout() => new AboutWindow { Owner = Main }.ShowDialog();

    public static void ShowRestHistory() => new RestHistoryWindow { Owner = Main }.ShowDialog();

    public static void ShowEsExamples() => new EsExamplesWindow { Owner = Main }.ShowDialog();

    // ---------- 消息 ----------
    public static void Error(Window? owner, string message)
        => MessageBox.Show(owner ?? Main, message, Localization.L("common.error"), MessageBoxButton.OK, MessageBoxImage.Error);

    public static void Info(Window? owner, string message)
        => MessageBox.Show(owner ?? Main, message, Localization.L("common.info"), MessageBoxButton.OK, MessageBoxImage.Information);

    public static bool Confirm(Window? owner, string message)
        => MessageBox.Show(owner ?? Main, message, Localization.L("common.confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static void Toast(string message) => Main?.ShowToast(message);

    public static void SetBusy(bool busy) => Main?.SetBusy(busy);

    // ---------- 页面跳转 ----------
    public static void NavigateTo(string pageCode) => Main?.NavigateTo(pageCode);

    public static void NavigateSearch(string indexName) => Main?.NavigateSearch(indexName);
}