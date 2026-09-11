using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VaporVault.Core.Data;
using VaporVault.Core.Lifecycle;
using VaporVault.Core.Models;
using VaporVault.Core.Quarantine;
using System.Collections.ObjectModel;

namespace VaporVault_App.ViewModels;

/// <summary>
/// ViewModel for the Vault page — displays all currently quarantined apps
/// with days remaining, size, and one-click Revive or Delete Now per item.
/// </summary>
public partial class VaultViewModel : ObservableObject
{
    private readonly QuarantineIndex _quarantineIndex;
    private readonly ReviveService _reviveService;
    private readonly ExpiryChecker _expiryChecker;
    private readonly HistoryLogger _historyLogger;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasItems;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _vaultUsageSummary = "";

    [ObservableProperty]
    private double _vaultUsagePercent;

    public ObservableCollection<QuarantineEntry> Entries { get; } = [];

    public VaultViewModel()
    {
        _quarantineIndex = new QuarantineIndex();
        _reviveService = new ReviveService(_quarantineIndex);
        _expiryChecker = new ExpiryChecker(_quarantineIndex);
        _historyLogger = new HistoryLogger();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        Entries.Clear();

        await Task.Run(() =>
        {
            // Process any expired entries on load
            var deleted = _expiryChecker.ProcessExpiredEntries(_historyLogger);
            if (deleted > 0)
            {
                StatusMessage = $"{deleted} expired quarantine(s) were permanently deleted.";
            }
        });

        var activeEntries = _quarantineIndex.GetActive();

        foreach (var entry in activeEntries)
        {
            Entries.Add(entry);
        }

        HasItems = Entries.Count > 0;

        // v3: Update vault usage summary
        UpdateVaultUsage(activeEntries);

        if (!HasItems && string.IsNullOrEmpty(StatusMessage))
        {
            StatusMessage = "No quarantined apps. Use the Scan page to find leftover files.";
        }

        IsLoading = false;
    }

    [RelayCommand]
    private async Task ReviveAsync(QuarantineEntry entry)
    {
        IsProcessing = true;
        StatusMessage = $"Restoring {entry.AppName}...";

        try
        {
            var result = await _reviveService.ReviveAsync(entry.QuarantineId);

            if (result.Success)
            {
                Entries.Remove(entry);
                HasItems = Entries.Count > 0;

                var msg = $"✓ {result.AppName} restored ({result.FilesRestored} files).";
                if (result.RegistryFileAvailable)
                    msg += " Registry file available for manual import.";

                StatusMessage = msg;

                _historyLogger.LogRevive(entry);
            }
            else
            {
                StatusMessage = $"Restore failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Restore failed: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task DeleteNowAsync(QuarantineEntry entry)
    {
        IsProcessing = true;
        StatusMessage = $"Permanently deleting {entry.AppName}...";

        try
        {
            await Task.Run(() =>
            {
                _expiryChecker.PermanentlyDelete(entry.QuarantineId, _historyLogger);
            });

            Entries.Remove(entry);
            HasItems = Entries.Count > 0;
            StatusMessage = $"✓ {entry.AppName} permanently deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Delete failed: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void UpdateVaultUsage(IReadOnlyList<QuarantineEntry> activeEntries)
    {
        var totalUsage = activeEntries.Sum(e => e.TotalSizeBytes);
        var settings = AppSettings.Load();
        var cap = settings.MaxQuarantineSizeBytes;

        if (cap <= 0)
        {
            VaultUsageSummary = $"{FormatSize(totalUsage)} used (no cap)";
            VaultUsagePercent = 0;
        }
        else
        {
            var percent = Math.Min((double)totalUsage / cap * 100, 100);
            VaultUsageSummary = $"{FormatSize(totalUsage)} / {FormatSize(cap)} used ({percent:F0}%)";
            VaultUsagePercent = percent;
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
    };
}
