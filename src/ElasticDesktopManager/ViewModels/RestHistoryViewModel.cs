using ElasticDesktopManager.Core.Models;
using ElasticDesktopManager.Mvvm;
using ElasticDesktopManager.Services;

namespace ElasticDesktopManager.ViewModels;

public class RestHistoryViewModel : ObservableObject
{
    public ObservableList<CommandHistoryItem> Items { get; } = new();

    public bool HasItems => Items.Count > 0;

    public void Reload()
    {
        Items.ReplaceAll(App.HistoryService.Load());
        OnPropertyChanged(nameof(HasItems));
    }
}