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

        // ── Step 2: Check vault cap BEFORE quarantining (v3) ──
        var quarantineManager = new QuarantineManager();
        var capCheck = quarantineManager.CheckVaultCap(orphan.TotalSizeBytes);

        if (capCheck.WouldExceedCap)
        {
            var capDialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Vault storage cap exceeded",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Quarantining {orphan.AppName} ({FormatSize(orphan.TotalSizeBytes)}) would exceed your vault cap.",
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = $"Current usage: {FormatSize(capCheck.CurrentUsageBytes)} / {FormatSize(capCheck.CapBytes)}\n" +
                                   $"Space needed: {FormatSize(capCheck.SpaceNeededBytes)}",
                            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = capCheck.OldestEntry != null
                                ? $"Tip: You can free space by deleting the oldest entry \"{capCheck.OldestEntry.AppName}\" from the Vault page."
                                : "Tip: Delete old entries from the Vault page or increase the cap in Settings.",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                        }
                    }
                },
                PrimaryButtonText = "Quarantine Anyway",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var capResult = await capDialog.ShowAsync();
            if (capResult != ContentDialogResult.Primary)
                return;
        }

        // ── Step 3: Perform the quarantine ──
        await ViewModel.QuarantineCommand.ExecuteAsync(orphan);

        // ── Step 4: Check for additional traces and offer to disable (v3) ──
        try
        {
            // Re-read manifest to get the AdditionalTraces that were detected during quarantine
            var manifestWriter = new VaporVault.Core.Quarantine.ManifestWriter();

            // Find the quarantine folder — it's the most recent entry for this app
            var index = new VaporVault.Core.Data.QuarantineIndex();
            var latestEntry = index.GetActive()
                .Where(e => e.AppName == orphan.AppName)
                .OrderByDescending(e => e.QuarantinedAt)
                .FirstOrDefault();

            if (latestEntry != null)
            {
                var manifest = manifestWriter.ReadManifest(latestEntry.QuarantineFolderPath);

                if (manifest?.AdditionalTraces?.HasAnyTraces == true)
                {
                    var traces = manifest.AdditionalTraces;
                    var traceLines = new List<string>();

                    foreach (var task in traces.ScheduledTasks)
                        traceLines.Add($"📅 Scheduled task: {task.Name}");
                    foreach (var svc in traces.Services)
                        traceLines.Add($"⚙️ Service: {svc.DisplayName}");
                    foreach (var assoc in traces.FileAssociations)
                        traceLines.Add($"📎 File association: {assoc.Extension} → {assoc.HandlerType}");
                    foreach (var dead in traces.DeadUninstallEntries)
                        traceLines.Add($"🗑️ Dead uninstall entry: {dead.DisplayName}");

                    var traceText = string.Join("\n", traceLines);

                    var traceDialog = new ContentDialog
                    {
                        XamlRoot = this.XamlRoot,
                        Title = $"Additional traces found for {orphan.AppName}",
                        Content = new StackPanel
                        {
                            Spacing = 8,
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = "VaporVault found leftover scheduled tasks, services, or registry entries that belong to this app:",
                                    TextWrapping = TextWrapping.Wrap
                                },
                                new ScrollViewer
                                {
                                    MaxHeight = 250,
                                    Content = new TextBlock
                                    {
                                        Text = traceText,
                                        TextWrapping = TextWrapping.Wrap,
                                        IsTextSelectionEnabled = true
                                    }
                                },
                                new TextBlock
                                {
                                    Text = "Would you like to disable them? Tasks will be disabled (not deleted) and services will be stopped.",
                                    TextWrapping = TextWrapping.Wrap,
                                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                                }
                            }
                        },
                        PrimaryButtonText = "Disable All",
                        CloseButtonText = "Skip",
                        DefaultButton = ContentDialogButton.Primary
                    };

                    var traceResult = await traceDialog.ShowAsync();

                    if (traceResult == ContentDialogResult.Primary)
                    {
                        await Task.Run(() =>
                        {
                            quarantineManager.DisableDetectedTraces(latestEntry.QuarantineFolderPath);
                        });

                        ViewModel.StatusMessage += " Additional traces disabled.";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Trace dialog error: {ex.Message}");
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
