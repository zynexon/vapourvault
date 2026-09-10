using Microsoft.UI.Xaml;
using VaporVault.Core.Data;
using VaporVault.Core.Detection;
using VaporVault_App.Services;

namespace VaporVault_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// v2 additions: UninstallWatcher, TrayIconService, and targeted-scan → toast pipeline.
/// </summary>
public partial class App : Application
{
    private MainWindow? _window;
    private NotificationService? _notificationService;
    private UninstallWatcher? _uninstallWatcher;
    private TrayIconService? _trayIconService;
    private OrphanScanner? _scanner;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "VaporVault", "watcher.log");

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
        var settings = AppSettings.Load();
        var isBackgroundLaunch = Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));


        _window = new MainWindow();

        // Only show the window if this isn't a --background (startup) launch
        if (!isBackgroundLaunch)
        {
            _window.Activate();
        }

        // Start the background notification + expiry service (v1).
        // Processes expired entries on startup, sends Day-25 toasts,
        // and rechecks every 6 hours while the app is running.
        _notificationService = new NotificationService();
        _notificationService.Initialize();

        // v2: Start live interception pipeline if enabled
        if (settings.LiveInterceptionEnabled)
        {
            StartLiveInterception();
        }
    }



    /// <summary>
    /// Initializes and starts the live uninstall interception pipeline:
    /// 1. UninstallWatcher monitors registry for removals
    /// 2. On removal → targeted OrphanScanner finds leftovers
    /// 3. If leftovers found → toast notification
    /// 4. TrayIconService provides background presence
    /// </summary>
    private void StartLiveInterception()
    {
        // Create the targeted scanner (reuses existing detection pipeline)
        _scanner = new OrphanScanner();

        // Create and start the registry watcher
        _uninstallWatcher = new UninstallWatcher();
        _uninstallWatcher.AppsUninstalled += OnAppsUninstalled;

        try
        {
            _uninstallWatcher.Start();
            Log("UninstallWatcher started successfully.");
        }
        catch (Exception ex)
        {
            Log($"Failed to start UninstallWatcher: {ex.Message}");
        }

        // Create and show the tray icon
        _trayIconService = new TrayIconService();
        _trayIconService.OpenRequested += () =>
        {
            _window?.DispatcherQueue.TryEnqueue(() =>
            {
                _window?.ShowAndBringToFront();
            });
        };
        _trayIconService.ExitRequested += () =>
        {
            Log("ExitRequested from tray");
            StopLiveInterception();
            _notificationService?.Stop();
            Microsoft.UI.Xaml.Application.Current.Exit();
        };
        _trayIconService.Show();
        
        NotificationService.ToastClicked += () =>
        {
            _window?.DispatcherQueue.TryEnqueue(() =>
            {
                _window?.ShowAndBringToFront();
            });
        };
    }

    /// <summary>
    /// Handles the AppsUninstalled event from UninstallWatcher.
    /// Runs a targeted scan for each uninstalled app and sends a toast if leftovers are found.
    /// This runs on a thread pool thread (from the watcher's debounce timer).
    /// </summary>
    private void OnAppsUninstalled(IReadOnlyList<string> appNames)
    {
        if (_scanner is null) return;

        Log($"AppsUninstalled event received for {appNames.Count} app(s): {string.Join(", ", appNames)}");

        foreach (var appName in appNames)
        {
            try
            {
                Log($"Running targeted scan for uninstalled app: '{appName}'");

                var orphans = _scanner.ScanForApp(appName);

                if (orphans.Count > 0)
                {
                    var orphan = orphans[0]; // ScanForApp returns at most 1 grouped result
                    Log($"Found {orphan.Folders.Count} leftover folder(s) for '{appName}', " +
                        $"total size: {orphan.TotalSizeBytes} bytes. Sending toast...");

                    NotificationService.SendUninstallDetectedToast(
                        appName,
                        orphan.TotalSizeBytes,
                        orphan.Folders.Count);

                    Log($"Toast sent for '{appName}'.");
                }
                else
                {
                    Log($"No leftovers found for '{appName}'. Staying silent.");
                }
            }
            catch (Exception ex)
            {
                Log($"Error scanning for '{appName}': {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    /// <summary>
    /// Stops all live interception services and cleans up resources.
    /// </summary>
    private void StopLiveInterception()
    {
        _uninstallWatcher?.Dispose();
        _uninstallWatcher = null;

        _trayIconService?.Dispose();
        _trayIconService = null;

        _scanner = null;
    }

    /// <summary>
    /// Writes a timestamped log entry to the watcher log file.
    /// This replaces Debug.WriteLine so diagnostics are visible outside a debugger.
    /// </summary>
    internal static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (dir != null) Directory.CreateDirectory(dir);

            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            File.AppendAllText(LogPath, line);
        }
        catch
        {
            // Last resort — can't even log
        }

        System.Diagnostics.Debug.WriteLine($"VaporVault: {message}");
    }
}
