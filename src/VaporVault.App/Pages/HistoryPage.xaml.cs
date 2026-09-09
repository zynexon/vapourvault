using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VaporVault_App.ViewModels;

namespace VaporVault_App.Pages;

/// <summary>
/// History page — shows log of permanent deletions and revive actions.
/// </summary>
public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; } = new();

    public HistoryPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.LoadCommand.Execute(null);
    }
}
