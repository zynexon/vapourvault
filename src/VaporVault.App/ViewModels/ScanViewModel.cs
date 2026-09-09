using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VaporVault.Core.Detection;
using VaporVault.Core.Models;
using VaporVault.Core.Quarantine;
using System.Collections.ObjectModel;

namespace VaporVault_App.ViewModels;

/// <summary>
/// ViewModel for the Scan page — runs orphan detection and displays results as cards.
/// </summary>
public partial class ScanViewModel : ObservableObject
{
    private readonly OrphanScanner _scanner;
    private readonly QuarantineManager _quarantineManager;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private string _statusMessage = "Click \"Scan\" to find leftover files from uninstalled apps.";

    [ObservableProperty]
    private string _scanSummary = "";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isQuarantining;

    public ObservableCollection<OrphanedApp> OrphanedApps { get; } = [];

    public ScanViewModel()
    {
        _scanner = new OrphanScanner();
        _quarantineManager = new QuarantineManager();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsScanning = true;
        HasResults = false;
        StatusMessage = "Scanning for leftover files...";
        OrphanedApps.Clear();

        try
        {
            var result = await _scanner.ScanAsync();

            foreach (var orphan in result.OrphanedApps)
            {
                OrphanedApps.Add(orphan);
            }

            HasResults = OrphanedApps.Count > 0;

            if (HasResults)
            {
                var totalSize = FormatSize(result.TotalReclaimableBytes);
                StatusMessage = $"Found {OrphanedApps.Count} orphaned app(s) using {totalSize}.";
                ScanSummary = $"Scanned {result.TotalFoldersScanned} folders against {result.TotalInstalledAppsFound} installed apps in {result.ScanDuration.TotalSeconds:F1}s.";
            }
            else
            {
                StatusMessage = "No leftover files found. Your system is clean!";
                ScanSummary = $"Scanned {result.TotalFoldersScanned} folders in {result.ScanDuration.TotalSeconds:F1}s.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task QuarantineAsync(OrphanedApp orphan)
    {
        IsQuarantining = true;
        StatusMessage = $"Quarantining {orphan.AppName}...";

        try
        {
            var result = await _quarantineManager.QuarantineAsync(orphan, p =>
            {
                Progress = p * 100;
            });

            if (result.Success)
            {
                OrphanedApps.Remove(orphan);
                HasResults = OrphanedApps.Count > 0;

                var message = $"✓ {orphan.AppName} quarantined successfully ({result.TotalFilesMoved} files moved).";
                if (result.PendingRebootFiles > 0)
                    message += $" {result.PendingRebootFiles} file(s) will move on next restart.";

                StatusMessage = message;

                // Fire and forget background compression
                _ = Task.Run(async () =>
                {
                    var compressor = new CompressionService();
                    await compressor.CompressAsync(result.QuarantineFolderPath);
                });
            }
            else
            {
                StatusMessage = $"Quarantine failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Quarantine failed: {ex.Message}";
        }
        finally
        {
            IsQuarantining = false;
            Progress = 0;
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F1} GB"
    };
}
