using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VaporVault_App.ViewModels;
using VaporVault.Core.Models;

namespace VaporVault_App.Pages;

/// <summary>
/// Vault page — displays quarantined apps with Revive and Delete Now actions.
/// </summary>
public sealed partial class VaultPage : Page
{
    public VaultViewModel ViewModel { get; } = new();

    public VaultPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private async void ReviveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is QuarantineEntry entry)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = $"Restore {entry.AppName}?",
                Content = "This will move all quarantined files back to their original locations.\n" +
                          "Registry keys will NOT be automatically restored — you'll be offered the option separately.",
                PrimaryButtonText = "Restore",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.ReviveCommand.ExecuteAsync(entry);
            }
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is QuarantineEntry entry)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = $"Permanently Delete {entry.AppName}?",
                Content = "This will permanently delete all quarantined files. This action cannot be undone.",
                PrimaryButtonText = "Delete Permanently",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteNowCommand.ExecuteAsync(entry);
            }
        }
    }
}
