using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VaporVault.Core.Lifecycle;
using System.Collections.ObjectModel;

namespace VaporVault_App.ViewModels;

/// <summary>
/// ViewModel for the History page — displays log of permanent deletions and revives.
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryLogger _historyLogger;

    [ObservableProperty]
    private bool _hasItems;

    [ObservableProperty]
    private string _statusMessage = "";

    public ObservableCollection<HistoryLogger.HistoryEntry> Entries { get; } = [];

    public HistoryViewModel()
    {
        _historyLogger = new HistoryLogger();
    }

    [RelayCommand]
    private void Load()
    {
        Entries.Clear();

        var history = _historyLogger.GetHistory();

        foreach (var entry in history)
        {
            Entries.Add(entry);
        }

        HasItems = Entries.Count > 0;
        StatusMessage = HasItems
            ? $"Showing {Entries.Count} history entries."
            : "No history yet. Quarantine some apps to see actions here.";
    }
}
