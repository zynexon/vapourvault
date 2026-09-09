using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VaporVault_App.ViewModels;
using VaporVault.Core.Models;

namespace VaporVault_App.Pages;

/// <summary>
/// Scan page — runs orphan detection and shows results as plain-English cards.
/// </summary>
public sealed partial class ScanPage : Page
{
    public ScanViewModel ViewModel { get; } = new();

    public ScanPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Ready state — user clicks Scan to start
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ScanCommand.ExecuteAsync(null);
    }

    private async void QuarantineButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is OrphanedApp orphan)
        {
            // Confirm with user before quarantining
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = $"Quarantine {orphan.AppName}?",
                Content = $"This will move {orphan.Folders.Count} folder(s) to a safe quarantine location.\n" +
                          $"Files can be restored within 30 days.\n\n" +
                          $"Total size: {FormatSize(orphan.TotalSizeBytes)}",
                PrimaryButtonText = "Quarantine",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.QuarantineCommand.ExecuteAsync(orphan);
            }
        }
    }

    private async void ShowPathsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is OrphanedApp orphan)
        {
            var paths = string.Join("\n", orphan.Folders.Select(f =>
                $"• {f.Location}: {f.FullPath} ({FormatSize(f.SizeBytes)})"));

            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = $"{orphan.AppName} — File Paths",
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = paths,
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true
                    },
                    MaxHeight = 400
                },
                CloseButtonText = "Close"
            };

            await dialog.ShowAsync();
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
