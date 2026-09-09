using Microsoft.UI.Xaml;
using VaporVault_App.Services;

namespace VaporVault_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private NotificationService? _notificationService;

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();

        // Start the background notification + expiry service.
        // Processes expired entries on startup, sends Day-25 toasts,
        // and rechecks every 6 hours while the app is running.
        _notificationService = new NotificationService();
        _notificationService.Initialize();
    }
}
