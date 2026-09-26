using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VaporVault.Core.Data;
using VaporVault_App.Services;
using Windows.ApplicationModel;

namespace VaporVault_App.Pages;

/// <summary>
/// Settings page — controls live interception, startup, and close-to-tray behavior.
/// </summary>
public sealed partial class SettingsPage : Page
{
    private AppSettings _settings = null!;
    private bool _isLoading = true; // Suppress Toggled events during initial load

    public SettingsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoading = true;

        _settings = AppSettings.Load();

        LiveInterceptionToggle.IsOn = _settings.LiveInterceptionEnabled;
        CloseToTrayToggle.IsOn = _settings.CloseToTray;
        CloseToTrayToggle.IsEnabled = _settings.LiveInterceptionEnabled;

        // Query the actual MSIX StartupTask state from Windows
        var startupState = await StartupManager.GetStateAsync();
        RunAtStartupToggle.IsOn = StartupManager.IsEnabled(startupState);
        UpdateStartupStatus(startupState);

        // Vault cap: convert bytes to GB for the slider
        if (_settings.MaxQuarantineSizeBytes <= 0)
        {
            VaultCapSlider.Value = 0;
            VaultCapLabel.Text = "Unlimited";
        }
        else
        {
            var gb = (double)_settings.MaxQuarantineSizeBytes / (1024L * 1024 * 1024);
            VaultCapSlider.Value = Math.Clamp(gb, 0, 50);
            VaultCapLabel.Text = $"{(int)gb} GB";
        }

        _isLoading = false;
    }

    private void LiveInterceptionToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        _settings.LiveInterceptionEnabled = LiveInterceptionToggle.IsOn;
        _settings.Save();

        // Update Close to Tray availability
        CloseToTrayToggle.IsEnabled = LiveInterceptionToggle.IsOn;
        if (!LiveInterceptionToggle.IsOn)
        {
            CloseToTrayToggle.IsOn = false;
            _settings.CloseToTray = false;
            _settings.Save();
        }

        // The App class will pick up the setting change on next startup.
        // For immediate effect, we'd need to signal the App to start/stop the watcher.
        // That wiring happens in App.xaml.cs via the static Settings property.
    }

    private void CloseToTrayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        _settings.CloseToTray = CloseToTrayToggle.IsOn;
        _settings.Save();
    }

    private async void RunAtStartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        if (RunAtStartupToggle.IsOn)
        {
            var result = await StartupManager.EnableAsync();
            if (!StartupManager.IsEnabled(result))
            {
                // Enable failed — revert the toggle without re-firing this handler
                _isLoading = true;
                RunAtStartupToggle.IsOn = false;
                _isLoading = false;
            }
            UpdateStartupStatus(result);
        }
        else
        {
            await StartupManager.DisableAsync();
            UpdateStartupStatus(StartupTaskState.Disabled);
        }

        _settings.RunAtStartup = RunAtStartupToggle.IsOn;
        _settings.Save();
    }

    /// <summary>
    /// Shows or hides the startup status InfoBar based on the StartupTask state.
    /// Surfaces DisabledByUser/DisabledByPolicy so the user knows why
    /// the toggle won't stick — a state the old registry approach never exposed.
    /// </summary>
    private void UpdateStartupStatus(StartupTaskState state)
    {
        var reason = StartupManager.GetDisabledReason(state);
        StartupInfoBar.IsOpen = reason != null;
        if (reason != null)
        {
            StartupInfoBar.Message = reason;
        }
    }

    private void VaultCapSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading) return;

        var gbValue = (int)VaultCapSlider.Value;

        if (gbValue == 0)
        {
            VaultCapLabel.Text = "Unlimited";
            _settings.MaxQuarantineSizeBytes = 0;
        }
        else
        {
            VaultCapLabel.Text = $"{gbValue} GB";
            _settings.MaxQuarantineSizeBytes = (long)gbValue * 1024 * 1024 * 1024;
        }

        _settings.Save();
    }
}
