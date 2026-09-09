using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VaporVault_App.ViewModels;
using VaporVault.Core.Models;
using VaporVault.Core.Quarantine;
using VaporVault.Core.Interop;

namespace VaporVault_App.Pages;

/// <summary>
/// Scan page — runs orphan detection and shows results as plain-English cards.
/// Implements the full safety UX: check for running processes → prompt to close → quarantine.
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
        if (sender is not Button button || button.Tag is not OrphanedApp orphan)
            return;

        // ── Step 1: Check for running processes BEFORE quarantining ──
        var runningProcesses = ProcessHelper.FindProcessesByAppName(orphan.AppName);

        if (runningProcesses.Count > 0)
        {
            var processNames = string.Join("\n",
                runningProcesses.Select(p =>
                {
                    try { return $"  • {p.ProcessName} (PID {p.Id})"; }
                    catch { return "  • (unknown process)"; }
                }));

            var processDialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = $"{orphan.AppName} still has running processes",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "The following processes are still running and may be holding files open:",
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = processNames,
                            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                            TextWrapping = TextWrapping.Wrap,
                            IsTextSelectionEnabled = true
                        },
                        new TextBlock
                        {
                            Text = "Close them now so VaporVault can move the files cleanly, " +
                                   "or proceed anyway (locked files will be scheduled for move on next restart).",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                        }
                    }
                },
                PrimaryButtonText = "Close & Quarantine",
                SecondaryButtonText = "Quarantine Anyway",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var processResult = await processDialog.ShowAsync();

            if (processResult == ContentDialogResult.None)
            {
                // User cancelled
                foreach (var p in runningProcesses) p.Dispose();
                return;
            }

            if (processResult == ContentDialogResult.Primary)
            {
                // User chose to close processes first
                int closed = ProcessHelper.CloseAllProcessesForApp(orphan.AppName);
                ViewModel.StatusMessage = $"Closed {closed} process(es). Proceeding with quarantine...";

                // Brief wait for handles to release
                await Task.Delay(1500);
            }

            // Secondary = quarantine anyway (proceed with locked-file fallback)
            foreach (var p in runningProcesses) p.Dispose();
        }
        else
        {
            // ── No running processes — show standard confirmation ──
            var confirmDialog = new ContentDialog
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

            var confirmResult = await confirmDialog.ShowAsync();
            if (confirmResult != ContentDialogResult.Primary)
                return;
        }

        // ── Step 2: Perform the quarantine ──
        await ViewModel.QuarantineCommand.ExecuteAsync(orphan);
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
